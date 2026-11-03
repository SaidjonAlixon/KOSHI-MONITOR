using System.Diagnostics;
using System.Runtime.InteropServices;
using KOSHI.Agent.Core;
using KOSHI.Contracts;
using Application = System.Windows.Application;

namespace KOSHI.Agent.Remote;

/// <summary>Shows the password prompt for a locked application and closes blocked/refused apps immediately.</summary>
public sealed class AppLockHost : IAppLockHost
{
    private const int SwMinimize = 6;
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    public bool ChallengeAndEnforce(AgentApplicationRuleDto rule)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return false;

        var handles = MinimizeTargetWindows(rule.ProcessName);

        var allowed = dispatcher.Invoke(() =>
        {
            var dialog = new AppLockWindow(rule.Name, rule.ProcessName, pwd => AppLock.Verify(rule, pwd));
            dialog.Activate();
            return dialog.ShowDialog() == true;
        });

        if (allowed)
        {
            foreach (var h in handles)
            {
                ShowWindow(h, SwRestore);
                SetForegroundWindow(h);
            }
            return true;
        }

        TerminateApp(rule.ProcessName);
        return false;
    }

    public void TerminateApp(string processName)
    {
        var target = NormalizeName(processName);
        if (string.IsNullOrEmpty(target)) return;
        var session = Process.GetCurrentProcess().SessionId;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!string.Equals(NormalizeName(p.ProcessName), target, StringComparison.OrdinalIgnoreCase)) continue;
                if (!SameSession(p, session)) continue;
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch
                {
                    p.Kill();
                }
            }
            catch
            {
                // Ignore processes that already exited or are elevated system processes.
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private static List<nint> MinimizeTargetWindows(string processName)
    {
        var handles = new List<nint>();
        var target = NormalizeName(processName);
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!string.Equals(NormalizeName(p.ProcessName), target, StringComparison.OrdinalIgnoreCase)) continue;
                if (!SameSession(p, session)) continue;
                var h = p.MainWindowHandle;
                if (h != 0)
                {
                    ShowWindow(h, SwMinimize);
                    handles.Add(h);
                }
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
        return handles;
    }

    private static string NormalizeName(string name) =>
        System.IO.Path.GetFileNameWithoutExtension(name.Trim());

    private static bool SameSession(Process p, int session)
    {
        try { return p.SessionId == session; }
        catch { return false; }
    }
}
