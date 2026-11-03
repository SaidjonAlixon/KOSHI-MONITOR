using System.Diagnostics;
using KOSHI.Agent.Core;
using KOSHI.Contracts;
using Application = System.Windows.Application;

namespace KOSHI.Agent.Remote;

/// <summary>Shows the password prompt for a locked application and closes it if the password is wrong or cancelled.</summary>
public sealed class AppLockHost : IAppLockHost
{
    public bool ChallengeAndEnforce(AgentApplicationRuleDto rule)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return false;

        var allowed = dispatcher.Invoke(() =>
        {
            var dialog = new AppLockWindow(rule.Name, rule.ProcessName, pwd => AppLock.Verify(rule, pwd));
            return dialog.ShowDialog() == true;
        });

        if (!allowed) Terminate(rule.ProcessName);
        return allowed;
    }

    private static void Terminate(string processName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(processName);
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName(name).Where(p => SameSession(p, session)))
        {
            try
            {
                if (!p.CloseMainWindow() || !p.WaitForExit(2000)) p.Kill();
            }
            catch (Exception)
            {
                // Access denied (elevated app) — nothing more we can do from the user session.
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private static bool SameSession(Process p, int session)
    {
        try { return p.SessionId == session; }
        catch (InvalidOperationException) { return false; }
    }
}
