using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace KOSHI.Agent.Service;

/// <summary>
/// Keeps the visible tray agent running in the interactive user session. Runs as LocalSystem inside the
/// Windows service, so even if the user ends KOSHI.Agent.exe from Task Manager it is relaunched within seconds.
/// Launches the agent with the logged-in user's token (not SYSTEM) so the tray icon and window are visible.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionAgentWatchdog(ILogger<SessionAgentWatchdog> logger) : BackgroundService
{
    private const string AgentProcess = "KOSHI.Agent";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var exe = ResolveAgentPath();
        if (exe is null)
        {
            logger.LogWarning("Tray agent executable not found next to the service; watchdog disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                EnsureRunning(exe);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Watchdog tick failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private void EnsureRunning(string exe)
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF) return; // no one logged in at the console

        // Already running in that session? (service is SYSTEM, so it can see all sessions)
        foreach (var p in Process.GetProcessesByName(AgentProcess))
        {
            try
            {
                if ((uint)p.SessionId == sessionId) return;
            }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }

        LaunchInSession(sessionId, exe);
    }

    private void LaunchInSession(uint sessionId, string exe)
    {
        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            logger.LogDebug("WTSQueryUserToken failed: {Err}", Marshal.GetLastWin32Error());
            return;
        }

        var dupOk = DuplicateTokenEx(userToken, 0x02000000 /* MAXIMUM_ALLOWED */, IntPtr.Zero,
            SecurityImpersonation, TokenPrimary, out var primaryToken);
        CloseHandle(userToken);
        if (!dupOk) return;

        var envOk = CreateEnvironmentBlock(out var env, primaryToken, false);
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
        const uint CreateUnicodeEnvironment = 0x00000400;
        const uint CreateNoWindow = 0x08000000;

        try
        {
            var ok = CreateProcessAsUser(primaryToken, exe, $"\"{exe}\"", IntPtr.Zero, IntPtr.Zero, false,
                CreateUnicodeEnvironment | CreateNoWindow, env, Path.GetDirectoryName(exe), ref si, out var pi);
            if (!ok)
            {
                logger.LogDebug("CreateProcessAsUser failed: {Err}", Marshal.GetLastWin32Error());
                return;
            }
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            logger.LogInformation("Tray agent launched in session {Session}", sessionId);
        }
        finally
        {
            if (envOk) DestroyEnvironmentBlock(env);
            CloseHandle(primaryToken);
        }
    }

    private static string? ResolveAgentPath()
    {
        var dir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(dir, "..", "agent", "KOSHI.Agent.exe"),
            Path.Combine(dir, "KOSHI.Agent.exe"),
        ];
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(IntPtr existingToken, uint desiredAccess, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr env);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? appName, string cmdLine, IntPtr procAttrs, IntPtr threadAttrs,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr env, string? curDir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }
}
