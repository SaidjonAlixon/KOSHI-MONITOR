using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KOSHI.Agent.Core;
using KOSHI.Contracts;
using Application = System.Windows.Application;

namespace KOSHI.Agent.Remote;

/// <summary>
/// Strictly locks password-protected applications:
/// 1) Hides all windows of the target process (SW_HIDE) so they vanish from screen and taskbar.
/// 2) Freezes the process at the OS kernel level (NtSuspendProcess) while the password dialog is shown,
///    making it impossible to interact with or restore via taskbar/tray/Alt+Tab.
/// 3) Resumes (NtResumeProcess) and restores windows ONLY if the correct password is entered;
///    otherwise kills the entire process tree immediately.
/// </summary>
public sealed class AppLockHost : IAppLockHost
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const uint ProcessSuspendResume = 0x0800;

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(nint processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(nint processHandle);

    public bool HasActiveVisibleWindow(string processName)
    {
        var pids = GetMatchingSessionPids(processName);
        if (pids.Count == 0) return false;

        var foundVisible = false;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pids.Contains(pid))
            {
                foundVisible = true;
                return false;
            }
            return true;
        }, 0);
        return foundVisible;
    }

    public bool ChallengeAndEnforce(AgentApplicationRuleDto rule)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return false;

        var hiddenHandles = new HashSet<nint>();
        var suspendedPids = new HashSet<uint>();

        LockAndFreezeTarget(rule.ProcessName, hiddenHandles, suspendedPids);

        var allowed = dispatcher.Invoke(() =>
        {
            var guardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            guardTimer.Tick += (_, _) => LockAndFreezeTarget(rule.ProcessName, hiddenHandles, suspendedPids);
            guardTimer.Start();

            try
            {
                var dialog = new AppLockWindow(rule.Name, rule.ProcessName, pwd => AppLock.Verify(rule, pwd));
                dialog.Activate();
                return dialog.ShowDialog() == true;
            }
            finally
            {
                guardTimer.Stop();
            }
        });

        if (allowed)
        {
            ResumeProcesses(suspendedPids);
            foreach (var h in hiddenHandles)
            {
                ShowWindow(h, SwShow);
                ShowWindow(h, SwRestore);
                SetForegroundWindow(h);
            }
            return true;
        }

        // Terminate while still hidden and suspended so the window never flashes back
        TerminateApp(rule.ProcessName);
        return false;
    }

    public void TerminateApp(string processName)
    {
        var pids = GetMatchingSessionPids(processName);
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                try { p.Kill(entireProcessTree: true); }
                catch { p.Kill(); }
            }
            catch
            {
                // Fallback to taskkill below
            }
        }

        var target = NormalizeName(processName);
        if (!string.IsNullOrEmpty(target))
        {
            try
            {
                var exeName = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : $"{target}.exe";
                using var tk = Process.Start(new ProcessStartInfo("taskkill.exe", $"/F /T /IM \"{exeName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                tk?.WaitForExit(1500);
            }
            catch
            {
                // Ignore if taskkill fails
            }
        }
    }

    private static void LockAndFreezeTarget(string processName, HashSet<nint> hiddenHandles, HashSet<uint> suspendedPids)
    {
        var pids = GetMatchingSessionPids(processName);
        if (pids.Count == 0) return;

        // 1. Hide every visible window belonging to those PIDs
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pids.Contains(pid))
            {
                ShowWindow(hWnd, SwHide);
                hiddenHandles.Add(hWnd);
            }
            return true;
        }, 0);

        // 2. Suspend the processes at kernel level so they cannot re-show windows or process clicks
        foreach (var pid in pids)
        {
            if (!suspendedPids.Add(pid)) continue;
            var hProc = OpenProcess(ProcessSuspendResume, false, pid);
            if (hProc != 0)
            {
                try { _ = NtSuspendProcess(hProc); }
                finally { CloseHandle(hProc); }
            }
        }
    }

    private static void ResumeProcesses(HashSet<uint> pids)
    {
        foreach (var pid in pids)
        {
            var hProc = OpenProcess(ProcessSuspendResume, false, pid);
            if (hProc != 0)
            {
                try { _ = NtResumeProcess(hProc); }
                finally { CloseHandle(hProc); }
            }
        }
    }

    private static HashSet<uint> GetMatchingSessionPids(string processName)
    {
        var pids = new HashSet<uint>();
        var target = NormalizeName(processName);
        if (string.IsNullOrEmpty(target)) return pids;
        var session = Process.GetCurrentProcess().SessionId;
        var selfPid = Process.GetCurrentProcess().Id;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == selfPid || !SameSession(p, session)) continue;
                if (MatchesTarget(p, target))
                    pids.Add((uint)p.Id);
            }
            catch
            {
                // Ignore inaccessible processes
            }
            finally
            {
                p.Dispose();
            }
        }
        return pids;
    }

    private static bool MatchesTarget(Process p, string target)
    {
        if (string.Equals(NormalizeName(p.ProcessName), target, StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            var vi = p.MainModule?.FileVersionInfo;
            if (vi is null) return false;
            if (!string.IsNullOrWhiteSpace(vi.OriginalFilename) && string.Equals(NormalizeName(vi.OriginalFilename), target, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrWhiteSpace(vi.InternalName) && string.Equals(NormalizeName(vi.InternalName), target, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // Ignore module access errors
        }
        return false;
    }

    private static string NormalizeName(string name) =>
        System.IO.Path.GetFileNameWithoutExtension(name.Trim());

    private static bool SameSession(Process p, int session)
    {
        try { return p.SessionId == session; }
        catch { return false; }
    }
}
