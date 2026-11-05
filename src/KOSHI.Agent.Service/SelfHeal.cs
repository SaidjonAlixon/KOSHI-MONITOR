using System.Diagnostics;
using KOSHI.Agent.Core;
using Microsoft.Win32;

namespace KOSHI.Agent.Service;

/// <summary>
/// Independent recovery when an employee kills the service (IFEO Debugger, sc delete, missing files).
/// A SYSTEM scheduled task keeps running even if KOSHI.Agent.Service.exe cannot start.
/// Only admin-panel uninstall removes the task.
/// </summary>
internal static class SelfHeal
{
    public const string TaskName = "KOSHIAgentHealth";
    public const string TaskLogon = "KOSHIHealthLogon";
    public const string TaskBoot = "KOSHIHealthBoot";
    public const string ServiceName = "KOSHIAgentService";
    public static readonly string[] ProtectedImages =
    [
        "KOSHI.Agent.exe", "KOSHI.Agent.Service.exe",
    ];
    public static readonly string[] HealImages =
    [
        "cmd.exe", "powershell.exe", "pwsh.exe", "conhost.exe",
        "schtasks.exe", "sc.exe", "reg.exe", "timeout.exe", "net.exe",
    ];
    public static readonly string[] AllTaskNames = [TaskName, TaskLogon, TaskBoot];

    public static string ScriptPath => Path.Combine(AgentPaths.MachineDir, "heal.cmd");
    public static string UpgradeLockPath => Path.Combine(AgentPaths.MachineDir, "upgrade.lock");

    /// <summary>While a trusted KOSHI setup is replacing files, self-heal must not restart the service
    /// or re-create tasks — otherwise it fights the installer and the upgrade silently fails.</summary>
    public static bool UpgradeInProgress()
    {
        try
        {
            return File.Exists(UpgradeLockPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(UpgradeLockPath) < TimeSpan.FromMinutes(15);
        }
        catch { return false; }
    }

    /// <summary>Called when an upgrade starts: pause the healer tasks and the SCM auto-restart so the
    /// installer can stop the service and overwrite the files.</summary>
    public static void BeginUpgrade()
    {
        try
        {
            Directory.CreateDirectory(AgentPaths.MachineDir);
            File.WriteAllText(UpgradeLockPath, DateTime.UtcNow.ToString("o"));
        }
        catch { /* best-effort */ }
        foreach (var name in AllTaskNames)
            RunHidden("schtasks.exe", $"/Change /TN \"{name}\" /DISABLE");
        RunHidden("sc.exe", $"failure {ServiceName} reset= 0 actions= \"\"");
    }

    public static void ClearUpgradeLock()
    {
        try { if (File.Exists(UpgradeLockPath)) File.Delete(UpgradeLockPath); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Called when the upgrade window ends: restore the healer tasks and SCM auto-restart.</summary>
    public static void EndUpgrade()
    {
        ClearUpgradeLock();
        foreach (var name in AllTaskNames)
            RunHidden("schtasks.exe", $"/Change /TN \"{name}\" /ENABLE");
        RunHidden("sc.exe", $"failure {ServiceName} reset= 0 actions= restart/1000/restart/1000/restart/1000");
    }

    public static bool IsHealProcess(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return false;
        return commandLine.Contains(TaskName, StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains(TaskLogon, StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains(TaskBoot, StringComparison.OrdinalIgnoreCase)
               || commandLine.Contains(@"\KOSHI\heal.cmd", StringComparison.OrdinalIgnoreCase)
               || (commandLine.Contains("heal.cmd", StringComparison.OrdinalIgnoreCase)
                   && commandLine.Contains("KOSHI", StringComparison.OrdinalIgnoreCase));
    }

    public static void Tick()
    {
        // Never restart/recreate while a trusted setup is replacing files.
        if (UpgradeInProgress())
        {
            ClearHijacks();
            return;
        }
        ClearHijacks();
        EnsureService();
        EnsureTask();
    }

    public static void Run()
    {
        ClearHijacks();
        EnsureService();
        EnsureTask();
        if (!File.Exists(ServiceExe()))
            ReinstallFromServer();
    }

    public static void Unregister()
    {
        foreach (var name in AllTaskNames)
        {
            try { RunHidden("schtasks.exe", $"/Delete /TN \"{name}\" /F"); } catch { /* gone */ }
        }
        try { File.Delete(ScriptPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <returns>True if an IFEO Debugger / SilentProcessExit hijack was present and cleared.</returns>
    public static bool ClearHijacks()
    {
        var found = false;
        foreach (var hive in new[]
                 {
                     @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                 })
        {
            foreach (var image in ProtectedImages)
                found |= ClearIfeo(hive, image);
        }

        foreach (var image in ProtectedImages)
        {
            try
            {
                using var existing = Registry.LocalMachine.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SilentProcessExit\{image}");
                if (existing is not null) found = true;
                Registry.LocalMachine.DeleteSubKeyTree(
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SilentProcessExit\{image}",
                    throwOnMissingSubKey: false);
            }
            catch (UnauthorizedAccessException) { }
        }

        return found;
    }

    private static bool ClearIfeo(string hive, string image)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.CreateSubKey(hive, true);
            if (baseKey is null) return false;
            using var key = baseKey.CreateSubKey(image, true);
            if (key is null) return false;
            var found = false;
            foreach (var value in new[] { "Debugger", "VerifierDlls", "VerifierFlags", "GlobalFlag", "AppCompatFlags" })
            {
                if (key.GetValue(value) is not null) found = true;
                key.DeleteValue(value, throwOnMissingValue: false);
            }
            key.SetValue("KoshiProtected", 1, RegistryValueKind.DWord);
            return found;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void EnsureService()
    {
        var exe = ServiceExe();
        if (!File.Exists(exe)) return;
        RunHidden("sc.exe", $"create {ServiceName} binPath= \"{exe}\" start= auto DisplayName= \"KOSHI Agent Service\"");
        RunHidden("sc.exe", $"config {ServiceName} binPath= \"{exe}\" start= auto");
        RunHidden("sc.exe", $"failure {ServiceName} reset= 0 actions= restart/1000/restart/1000/restart/1000");
        RunHidden("sc.exe", $"start {ServiceName}");
    }

    private static void EnsureTask()
    {
        WriteScript();
        var tr = $"cmd.exe /c {ScriptPath}";
        RunHidden("schtasks.exe",
            $"/Create /TN \"{TaskName}\" /SC MINUTE /MO 1 /RU SYSTEM /RL HIGHEST /F /NP /TR \"{tr}\"");
        RunHidden("schtasks.exe",
            $"/Create /TN \"{TaskLogon}\" /SC ONLOGON /RU SYSTEM /RL HIGHEST /F /NP /TR \"{tr}\"");
        RunHidden("schtasks.exe",
            $"/Create /TN \"{TaskBoot}\" /SC ONSTART /RU SYSTEM /RL HIGHEST /F /NP /TR \"{tr}\"");
    }

    private static void WriteScript()
    {
        Directory.CreateDirectory(AgentPaths.MachineDir);
        const string ifeo = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        const string silent = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SilentProcessExit";
        var svc = ServiceExe();
        var setup = Path.Combine(Path.GetTempPath(), "KOSHI-Agent-Setup.exe");
        var lockFile = UpgradeLockPath;
        File.WriteAllText(ScriptPath, $$"""
            @echo off
            if exist "{{lockFile}}" exit /b 0
            for %%i in (1,2,3,4,5) do (
              if exist "{{lockFile}}" exit /b 0
              reg delete "{{ifeo}}\KOSHI.Agent.exe" /v Debugger /f >nul 2>&1
              reg delete "{{ifeo}}\KOSHI.Agent.Service.exe" /v Debugger /f >nul 2>&1
              reg delete "{{silent}}\KOSHI.Agent.exe" /f >nul 2>&1
              reg delete "{{silent}}\KOSHI.Agent.Service.exe" /f >nul 2>&1
              sc config {{ServiceName}} start= auto >nul 2>&1
              sc start {{ServiceName}} >nul 2>&1
              if not exist "{{svc}}" (
                curl.exe -fsSL -o "{{setup}}" "https://koshi.uz/downloads/KOSHI-Agent-Setup.exe" >nul 2>&1
                if exist "{{setup}}" start "" "{{setup}}" /VERYSILENT /NORESTART /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS
              )
              timeout /t 10 /nobreak >nul
            )
            """);
    }

    private static void ReinstallFromServer()
    {
        try
        {
            var cfg = AgentStore.LoadConfig();
            var baseUrl = (cfg?.ServerUrl ?? "https://koshi.uz").TrimEnd('/');
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var dest = AgentSelfUpdate.NewTempPath("heal");
            var bytes = http.GetByteArrayAsync(baseUrl + "/downloads/KOSHI-Agent-Setup.exe").GetAwaiter().GetResult();
            if (bytes.Length < 1_000_000) return;
            File.WriteAllBytes(dest, bytes);
            AgentSelfUpdate.Launch(dest, alreadyElevated: true);
        }
        catch
        {
            // Next scheduled tick retries
        }
    }

    private static string ServiceExe()
    {
        var nextToUs = Path.Combine(AppContext.BaseDirectory, "KOSHI.Agent.Service.exe");
        if (File.Exists(nextToUs)) return Path.GetFullPath(nextToUs);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KOSHI", "service", "KOSHI.Agent.Service.exe");
    }

    private static void RunHidden(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
            });
            p?.WaitForExit(8000);
        }
        catch (Exception)
        {
            // schtasks/sc may be missing in unusual SKUs
        }
    }
}
