using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using KOSHI.Agent.Core;
using Microsoft.Win32;

namespace KOSHI.Agent.Service;

/// <summary>
/// Stops every practical uninstall path: service stop/delete, taskkill of the service,
/// deleting Program Files\KOSHI, sc.exe/PowerShell/CMD targeting KOSHI, msiexec uninstall.
/// Official KOSHI-Agent-Setup.exe (auto-update or reinstall) is allowed through UpgradeGate.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class UninstallGuard(ILogger<UninstallGuard> logger) : BackgroundService
{
    private const string ServiceName = "KOSHIAgentService";
    private const string HardenedServiceSddl =
        "D:(D;;GA;;;BG)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCLCSWLOCRRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)";
    private const string UpgradeServiceSddl =
        "D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)";
    private const string HardenedRegistrySddl = "D:P(A;CI;KA;;;SY)(A;CI;KR;;;BA)";
    private static readonly TimeSpan HardenEvery = TimeSpan.FromSeconds(4);
    private static readonly Regex ScMutate = new(@"\b(stop|delete|config|sdset|failure)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly string[] MutationMarks =
    [
        "stop-service", "remove-service", "set-service", "stop-process", "remove-item", "uninstall-package",
        "sc.exe", "sc stop", "sc delete", "sc config", "sc sdset", "taskkill", "msiexec", "takeown", "icacls",
        "rmdir", "rd /s", "del /", " /x ", " /uninstall", "win32_product",
    ];

    private bool _upgradeApplied;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(HardenLoop(stoppingToken), InterceptLoop(stoppingToken));

    private async Task HardenLoop(CancellationToken ct)
    {
        var lastAssert = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var upgrade = UpgradeGate.IsOpen;
                if (upgrade != _upgradeApplied || (!upgrade && DateTime.UtcNow - lastAssert >= HardenEvery))
                {
                    ApplyServiceSddl(upgrade ? UpgradeServiceSddl : HardenedServiceSddl);
                    ApplyFileAcls(upgrade);
                    ApplyRegistrySddl(upgrade);
                    if (!upgrade) ApplyFailureRestart();
                    _upgradeApplied = upgrade;
                    lastAssert = DateTime.UtcNow;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Uninstall harden tick failed");
            }
            await Task.Delay(200, ct);
        }
    }

    private async Task InterceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TickIntercept();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Uninstall intercept tick failed");
            }
            await Task.Delay(150, ct);
        }
    }

    private void TickIntercept()
    {
        var setupSeen = false;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (IsTrustedSetup(p))
                {
                    setupSeen = true;
                    UpgradeGate.Enter(TimeSpan.FromMinutes(15));
                }
            }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        UpgradeGate.MarkSetup(setupSeen);
        if (UpgradeGate.IsOpen) return;

        var self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self || p.Id <= 4) continue;
                var name = p.ProcessName;
                if (!IsWatchedHost(name)) continue;
                var cmd = Native.CommandLine(p.Id);
                if (!IsDangerous(name, cmd)) continue;
                logger.LogWarning("Blocked uninstall attempt: {Name} {Cmd}", name, Truncate(cmd));
                try { p.Kill(entireProcessTree: true); }
                catch { try { p.Kill(); } catch { /* already gone */ } }
            }
            catch (InvalidOperationException) { }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to inspect process {Pid}", p.Id);
            }
            finally { p.Dispose(); }
        }
    }

    private static bool IsWatchedHost(string name) => name.ToLowerInvariant() switch
    {
        "sc" or "net" or "taskkill" or "cmd" or "powershell" or "pwsh" or "powershell_ise"
            or "wmic" or "msiexec" or "takeown" or "icacls" or "reg" => true,
        _ => false,
    };

    private static bool IsDangerous(string process, string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return false;
        var p = process.ToLowerInvariant();
        var c = cmd.ToLowerInvariant();
        if (!TargetsKoshi(c)) return false;

        return p switch
        {
            "sc" => ScMutate.IsMatch(c),
            "net" => c.Contains(" stop", StringComparison.Ordinal),
            "taskkill" => true,
            "msiexec" => c.Contains("/x", StringComparison.Ordinal) || c.Contains("/uninstall", StringComparison.Ordinal),
            "takeown" or "icacls" or "reg" => true,
            "wmic" => c.Contains("delete", StringComparison.Ordinal) || c.Contains("stop", StringComparison.Ordinal) || c.Contains("uninstall", StringComparison.Ordinal),
            _ => MutationMarks.Any(m => c.Contains(m, StringComparison.Ordinal)),
        };
    }

    private static bool TargetsKoshi(string cmd) =>
        cmd.Contains("koshi", StringComparison.OrdinalIgnoreCase)
        || cmd.Contains("koshiagentservice", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrustedSetup(Process p)
    {
        var name = p.ProcessName;
        if (name.StartsWith("KOSHI-Agent-Setup", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("is-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("_iu", StringComparison.OrdinalIgnoreCase))
            return ParentIsTrustedSetup(p.Id, 3);
        return false;
    }

    private static bool ParentIsTrustedSetup(int pid, int depth)
    {
        var current = pid;
        for (var i = 0; i < depth; i++)
        {
            var parent = Native.ParentPid(current);
            if (parent is not int ppid || ppid <= 4) return false;
            try
            {
                using var p = Process.GetProcessById(ppid);
                if (p.ProcessName.StartsWith("KOSHI-Agent-Setup", StringComparison.OrdinalIgnoreCase)) return true;
                current = ppid;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        return false;
    }

    private void ApplyServiceSddl(string sddl)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", $"sdset {ServiceName} {sddl}")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            p?.WaitForExit(4000);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "sc sdset failed");
        }
    }

    private void ApplyFailureRestart()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", $"failure {ServiceName} reset= 0 actions= restart/1000/restart/1000/restart/1000")
            {
                CreateNoWindow = true, UseShellExecute = false,
            });
            p?.WaitForExit(4000);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "sc failure failed");
        }
    }

    private void ApplyFileAcls(bool upgrade)
    {
        var installRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        var adminRights = upgrade ? "F" : "RX";
        ApplyIcacls(installRoot, adminRights);
        ApplyIcacls(AgentPaths.MachineDir, adminRights);
    }

    private void ApplyIcacls(string path, string adminRights)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("icacls.exe")
            {
                ArgumentList =
                {
                    path, "/inheritance:r",
                    "/grant:r", "*S-1-5-18:(OI)(CI)F",
                    "/grant:r", $"*S-1-5-32-544:(OI)(CI){adminRights}",
                    "/grant:r", "*S-1-5-32-545:(OI)(CI)RX",
                },
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            p?.WaitForExit(8000);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "icacls failed on {Path}", path);
        }
    }

    private void ApplyRegistrySddl(bool upgrade)
    {
        Native.SetObjectSddl(
            $@"MACHINE\SYSTEM\CurrentControlSet\Services\{ServiceName}",
            Native.SeRegistryKey,
            upgrade ? "D:P(A;CI;KA;;;SY)(A;CI;KA;;;BA)" : HardenedRegistrySddl);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName, writable: true);
            key?.SetValue("Start", 2, RegistryValueKind.DWord); // auto
            key?.SetValue("ErrorControl", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Service registry repair failed");
        }
    }

    private static string Truncate(string? s) => string.IsNullOrEmpty(s) ? "" : s.Length <= 240 ? s : s[..240];

    private static partial class Native
    {
        public const uint SeRegistryKey = 4;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int ProcessBasicInformationClass = 0;
        private const int ProcessCommandLineInformation = 60;
        private const uint DaclSecurityInformation = 4;
        private const uint SddlRevision1 = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            public nint ExitStatus;
            public nint PebBaseAddress;
            public nint AffinityMask;
            public nint BasePriority;
            public nint UniqueProcessId;
            public nint InheritedFromUniqueProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public nint Buffer;
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);

        [LibraryImport("ntdll.dll")]
        private static partial int NtQueryInformationProcess(nint handle, int infoClass, nint buffer, int length, out int returnLength);

        [LibraryImport("ntdll.dll")]
        private static partial int NtQueryInformationProcess(nint handle, int infoClass, ref ProcessBasicInformation info, int length, out int returnLength);

        [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out nint sd, out uint size);

        [LibraryImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetSecurityDescriptorDacl(nint sd, [MarshalAs(UnmanagedType.Bool)] out bool daclPresent, out nint dacl, [MarshalAs(UnmanagedType.Bool)] out bool defaulted);

        [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial uint SetNamedSecurityInfoW(string name, uint objectType, uint securityInfo, nint owner, nint group, nint dacl, nint sacl);

        [LibraryImport("kernel32.dll")]
        private static partial nint LocalFree(nint h);

        public static int? ParentPid(int pid)
        {
            int? parent = null;
            WithHandle(pid, h =>
            {
                var info = default(ProcessBasicInformation);
                if (NtQueryInformationProcess(h, ProcessBasicInformationClass, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) == 0)
                    parent = (int)info.InheritedFromUniqueProcessId;
            });
            return parent;
        }

        public static string? CommandLine(int pid)
        {
            string? cmd = null;
            WithHandle(pid, h =>
            {
                NtQueryInformationProcess(h, ProcessCommandLineInformation, 0, 0, out var len);
                if (len < Marshal.SizeOf<UnicodeString>()) return;
                var buf = Marshal.AllocHGlobal(len);
                try
                {
                    if (NtQueryInformationProcess(h, ProcessCommandLineInformation, buf, len, out _) != 0) return;
                    var us = Marshal.PtrToStructure<UnicodeString>(buf);
                    if (us.Buffer == 0 || us.Length == 0) return;
                    cmd = Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
                }
                finally { Marshal.FreeHGlobal(buf); }
            });
            return cmd;
        }

        public static void SetObjectSddl(string name, uint objectType, string sddl)
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, SddlRevision1, out var sd, out _)) return;
            try
            {
                if (!GetSecurityDescriptorDacl(sd, out var present, out var dacl, out _) || !present) return;
                SetNamedSecurityInfoW(name, objectType, DaclSecurityInformation, 0, 0, dacl, 0);
            }
            finally { LocalFree(sd); }
        }

        private static void WithHandle(int pid, Action<nint> action)
        {
            var h = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
            if (h == 0) return;
            try { action(h); }
            finally { CloseHandle(h); }
        }
    }
}

/// <summary>Opened by the auto-updater (same process) and when a trusted KOSHI installer is seen, so the upgrade can stop the service and replace files.</summary>
internal static class UpgradeGate
{
    private static long _untilTicks;
    private static int _setupSeen;

    public static bool IsOpen =>
        DateTime.UtcNow.Ticks < Interlocked.Read(ref _untilTicks) || Volatile.Read(ref _setupSeen) != 0;

    public static void Enter(TimeSpan duration)
    {
        var until = DateTime.UtcNow.Add(duration).Ticks;
        long current;
        do { current = Interlocked.Read(ref _untilTicks); }
        while (until > current && Interlocked.CompareExchange(ref _untilTicks, until, current) != current);
    }

    public static void MarkSetup(bool seen) => Volatile.Write(ref _setupSeen, seen ? 1 : 0);
}
