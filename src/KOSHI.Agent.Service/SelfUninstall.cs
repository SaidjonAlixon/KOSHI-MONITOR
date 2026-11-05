using System.Diagnostics;
using Microsoft.Win32;

namespace KOSHI.Agent.Service;

/// <summary>
/// Removes KOSHI from this computer after an authenticated admin-panel command.
/// Local users still cannot uninstall; only the SYSTEM service after a signed hub command.
/// </summary>
internal static class SelfUninstall
{
    private const string ServiceName = "KOSHIAgentService";
    private static int _started;

    public static bool Begin(IHostApplicationLifetime lifetime)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return false;
        UpgradeGate.AllowUninstall();
        _ = Task.Run(() => Run(lifetime));
        return true;
    }

    private static void Run(IHostApplicationLifetime lifetime)
    {
        try
        {
            Thread.Sleep(2000);
            try { SelfHeal.Unregister(); } catch { /* best-effort */ }
            try { AppLockerAdapter.Rollback(); } catch { /* best-effort */ }
            try { KOSHI.Agent.Core.AgentStore.Delete(); } catch { /* best-effort */ }

            var installRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            RelaxAcl(installRoot);
            RelaxAcl(KOSHI.Agent.Core.AgentPaths.MachineDir);
            KillTray();
            RemoveRunKey();
            RemoveShortcut(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            RemoveShortcut(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));

            var script = WriteRemover(installRoot);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/C \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        finally
        {
            try { lifetime.StopApplication(); }
            catch { Environment.Exit(0); }
        }
    }

    private static string WriteRemover(string installRoot)
    {
        var data = KOSHI.Agent.Core.AgentPaths.MachineDir;
        var script = Path.Combine(Path.GetTempPath(), $"koshi-remove-{Guid.NewGuid():N}.cmd");
        var q = (string p) => p.Replace("\"", "");
        File.WriteAllText(script, $"""
            @echo off
            timeout /t 5 /nobreak >nul
            taskkill /F /IM KOSHI.Agent.exe /T >nul 2>&1
            taskkill /F /IM KOSHI.Agent.Service.exe /T >nul 2>&1
            schtasks /Delete /TN KOSHIAgentHealth /F >nul 2>&1
            schtasks /Delete /TN KOSHIHealthLogon /F >nul 2>&1
            schtasks /Delete /TN KOSHIHealthBoot /F >nul 2>&1
            sc stop {ServiceName} >nul 2>&1
            sc delete {ServiceName} >nul 2>&1
            timeout /t 2 /nobreak >nul
            icacls "{q(installRoot)}" /grant *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F /T /C >nul 2>&1
            rmdir /s /q "{q(installRoot)}" >nul 2>&1
            rmdir /s /q "{q(data)}" >nul 2>&1
            del /q "%PUBLIC%\Desktop\KOSHI Agent.lnk" >nul 2>&1
            del /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\KOSHI Agent.lnk" >nul 2>&1
            del "%~f0"
            """);
        return script;
    }

    private static void KillTray()
    {
        foreach (var p in Process.GetProcessesByName("KOSHI.Agent"))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            finally { p.Dispose(); }
        }
    }

    private static void RemoveRunKey()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("KOSHI Agent", throwOnMissingValue: false);
        }
        catch (UnauthorizedAccessException) { }
    }

    private static void RemoveShortcut(string folder)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "KOSHI Agent.lnk", SearchOption.AllDirectories))
                try { File.Delete(f); } catch (IOException) { }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void RelaxAcl(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("icacls.exe")
            {
                ArgumentList = { path, "/grant:r", "*S-1-5-18:(OI)(CI)F", "/grant:r", "*S-1-5-32-544:(OI)(CI)F" },
                CreateNoWindow = true, UseShellExecute = false,
            });
            p?.WaitForExit(8000);
        }
        catch { /* remover script retries */ }
    }
}
