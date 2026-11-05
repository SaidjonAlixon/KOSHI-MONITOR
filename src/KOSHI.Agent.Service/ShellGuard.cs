using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace KOSHI.Agent.Service;

/// <summary>
/// Employees must not open or even see Command Prompt, PowerShell or Windows Terminal.
/// User-session processes are frozen then killed; Start/Win+X shortcuts are hidden.
/// cmd.exe / powershell.exe are never IFEO-blocked: session-0 heal and AppLocker still need them.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class ShellGuard
{
    public static readonly string[] Names =
    [
        "cmd", "powershell", "pwsh", "powershell_ise",
        "windowsterminal", "wt", "openconsole",
    ];

    public static readonly string[] IfeoImages =
    [
        "powershell_ise.exe", "WindowsTerminal.exe", "wt.exe", "OpenConsole.exe",
    ];

    private const string IfeoBase = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string Marker = "KoshiBlocked";
    private static DateTime _lastHarden;

    public static bool IsName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var n = Path.GetFileNameWithoutExtension(processName.Trim());
        return Names.Contains(n, StringComparer.OrdinalIgnoreCase);
    }

    public static void Tick(bool harden = false)
    {
        _ = harden;
        KillUserShells();
        if (DateTime.UtcNow - _lastHarden <= TimeSpan.FromSeconds(8)) return;
        ApplyIfeo();
        ApplyUserPolicies();
        HideShortcuts();
        _lastHarden = DateTime.UtcNow;
    }

    private static void KillUserShells()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId == 0) continue;
                if (!IsName(p.ProcessName)) continue;
                var cmd = UninstallGuard.ReadCommandLine(p.Id);
                if (SelfHeal.IsHealProcess(cmd)) continue;
                Native.Suspend(p.Id);
                try { p.Kill(entireProcessTree: true); }
                catch { try { p.Kill(); } catch { /* gone */ } }
            }
            catch { /* already gone */ }
            finally { p.Dispose(); }
        }
    }

    private static void ApplyIfeo()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.CreateSubKey(IfeoBase, true);
            if (baseKey is null) return;
            foreach (var exe in IfeoImages)
            {
                using var sub = baseKey.CreateSubKey(exe, true);
                if (sub is null) continue;
                sub.SetValue(Marker, 1, RegistryValueKind.DWord);
                sub.SetValue("Debugger", "systray.exe", RegistryValueKind.String);
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    private static void ApplyUserPolicies()
    {
        foreach (var sid in UserSids())
        {
            try
            {
                using var explPol = Registry.Users.CreateSubKey($@"{sid}\Software\Policies\Microsoft\Windows\Explorer", true);
                explPol?.SetValue("DontUsePowerShellOnWinX", 1, RegistryValueKind.DWord);

                using var disallow = Registry.Users.CreateSubKey($@"{sid}\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", true);
                disallow?.SetValue("DisallowRun", 1, RegistryValueKind.DWord);
                using var list = Registry.Users.CreateSubKey($@"{sid}\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", true);
                if (list is not null)
                {
                    var i = 1;
                    foreach (var exe in new[]
                             {
                                 "cmd.exe", "powershell.exe", "pwsh.exe", "powershell_ise.exe",
                                 "WindowsTerminal.exe", "wt.exe", "OpenConsole.exe",
                             })
                    {
                        list.SetValue(i.ToString(), exe, RegistryValueKind.String);
                        i++;
                    }
                }

                using var adv = Registry.Users.CreateSubKey($@"{sid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
                adv?.SetValue("DontUsePowerShellOnWinX", 1, RegistryValueKind.DWord);
            }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void HideShortcuts()
    {
        var roots = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
        };
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(@"C:\Users"))
            {
                roots.Add(Path.Combine(dir, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs"));
                roots.Add(Path.Combine(dir, @"AppData\Local\Microsoft\Windows\WinX"));
            }
        }
        catch (IOException) { }

        string[] marks = ["powershell", "pwsh", "command prompt", "cmd", "windows terminal", "terminal"];
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
            catch (Exception) { continue; }
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!marks.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;
                try { File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.Hidden | FileAttributes.System); }
                catch (Exception) { /* in use */ }
            }
        }
    }

    private static IEnumerable<string> UserSids()
    {
        foreach (var name in Registry.Users.GetSubKeyNames())
        {
            if (name.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("_Classes", StringComparison.OrdinalIgnoreCase))
                yield return name;
        }
    }

    private static partial class Native
    {
        private const uint ProcessSuspendResume = 0x0800;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);

        [LibraryImport("ntdll.dll")]
        private static partial int NtSuspendProcess(nint handle);

        public static void Suspend(int pid)
        {
            var h = OpenProcess(ProcessSuspendResume, false, (uint)pid);
            if (h == 0) return;
            try { _ = NtSuspendProcess(h); }
            finally { CloseHandle(h); }
        }
    }
}
