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
/// Official KOSHI-Agent-Setup.exe may replace files (upgrade) while it is running; uninstallers are always blocked.
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
        "set-itemproperty", "new-itemproperty", "new-item ", "remove-itemproperty", "debugger",
        "image file execution", "silentprocessexit", "verifierdlls",
        "unregister-scheduledtask", "schtasks", "disable-scheduledtask",
    ];

    private bool _upgradeApplied;
    private DateTime _lastHealTask;

    internal static string? ReadCommandLine(int pid) => Native.CommandLine(pid);

    internal static void ApplyKeySddl(string machineRelativePath, string sddl) =>
        Native.SetObjectSddl(machineRelativePath, Native.SeRegistryKey, sddl);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(HardenLoop(stoppingToken), InterceptLoop(stoppingToken));

    private async Task HardenLoop(CancellationToken ct)
    {
        var lastAssert = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (SelfHeal.ClearHijacks())
                    KillHijackWriters();
                var upgrade = UpgradeGate.IsOpen || SelfHeal.UpgradeInProgress();
                if (upgrade && !_upgradeApplied) SelfHeal.BeginUpgrade();
                if (!upgrade && _upgradeApplied) SelfHeal.EndUpgrade();
                if (!upgrade) ShellGuard.Tick(harden: true);
                if (upgrade != _upgradeApplied || (!upgrade && DateTime.UtcNow - lastAssert >= HardenEvery))
                {
                    ApplyServiceSddl(upgrade ? UpgradeServiceSddl : HardenedServiceSddl);
                    ApplyFileAcls(upgrade);
                    ApplyRegistrySddl(upgrade);
                    RemoveLeftoverUninstaller();
                    ProtectIfeoKeys();
                    if (!upgrade)
                    {
                        ApplyFailureRestart();
                        if (DateTime.UtcNow - _lastHealTask > TimeSpan.FromSeconds(45))
                        {
                            SelfHeal.Tick();
                            _lastHealTask = DateTime.UtcNow;
                        }
                    }
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
        if (UpgradeGate.IsUninstalling) return;

        var setupSeen = false;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (IsTrustedSetup(p))
                {
                    setupSeen = true;
                    UpgradeGate.Enter(TimeSpan.FromMinutes(15));
                    SelfHeal.BeginUpgrade();
                }
            }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        UpgradeGate.MarkSetup(setupSeen);

        var self = Environment.ProcessId;
        var upgrading = UpgradeGate.IsOpen;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self || p.Id <= 4) continue;
                if (Native.ParentPid(p.Id) == self) continue;
                var name = p.ProcessName;
                var cmd = Native.CommandLine(p.Id);
                if (SelfHeal.IsHealProcess(cmd)) continue;
                if (IsKoshiUninstaller(p, name, cmd))
                {
                    logger.LogWarning("Blocked KOSHI uninstaller: {Name} {Cmd}", name, Truncate(cmd));
                    KillProcess(p);
                    continue;
                }
                if (!IsWatchedHost(name)) continue;
                if (!IsDangerous(name, cmd)) continue;
                if (upgrading && IsUpgradeHelper(name, cmd)) continue;
                logger.LogWarning("Blocked uninstall attempt: {Name} {Cmd}", name, Truncate(cmd));
                KillProcess(p);
            }
            catch (InvalidOperationException) { }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to inspect process {Pid}", p.Id);
            }
            finally { p.Dispose(); }
        }
    }

    private static void KillProcess(Process p)
    {
        try { p.Kill(entireProcessTree: true); }
        catch { try { p.Kill(); } catch { /* already gone */ } }
    }

    private static bool IsWatchedHost(string name) => name.ToLowerInvariant() switch
    {
        "sc" or "net" or "taskkill" or "cmd" or "powershell" or "pwsh" or "powershell_ise"
            or "wmic" or "msiexec" or "takeown" or "icacls" or "reg" or "regedit" or "regedt32"
            or "schtasks" or "netsh"
            or "control" or "rundll32" or "mshta" => true,
        _ => name.StartsWith("unins", StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>Official KOSHI-Agent-Setup may stop the tray/service to replace files — never delete the service or run an uninstaller.</summary>
    private static bool IsUpgradeHelper(string process, string? cmd)
    {
        var p = process.ToLowerInvariant();
        var c = (cmd ?? "").ToLowerInvariant();
        return p switch
        {
            "sc" => !c.Contains(" delete ", StringComparison.Ordinal)
                    && (c.Contains(" stop ", StringComparison.Ordinal)
                        || c.Contains(" failure ", StringComparison.Ordinal)
                        || c.Contains(" config ", StringComparison.Ordinal)
                        || c.Contains(" sdset ", StringComparison.Ordinal)),
            "net" => c.Contains(" stop", StringComparison.Ordinal),
            "taskkill" => c.Contains("koshi.agent", StringComparison.Ordinal),
            "schtasks" => !c.Contains(" /delete", StringComparison.Ordinal)
                          && (c.Contains("/disable", StringComparison.Ordinal)
                              || c.Contains("/end", StringComparison.Ordinal)
                              || c.Contains("/change", StringComparison.Ordinal)),
            "icacls" => TargetsKoshi(c),
            _ => false,
        };
    }

    private static bool IsKoshiUninstaller(Process p, string name, string? cmd)
    {
        var n = name.ToLowerInvariant();
        var c = cmd ?? "";
        if (n.StartsWith("unins", StringComparison.Ordinal) && (TargetsKoshi(c) || PathLooksKoshi(p)))
            return true;
        if (n is "msiexec" && TargetsKoshi(c) && (c.Contains("/x", StringComparison.OrdinalIgnoreCase) || c.Contains("/uninstall", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }

    private static bool PathLooksKoshi(Process p)
    {
        try
        {
            var path = p.MainModule?.FileName;
            return !string.IsNullOrEmpty(path) && path.Contains("KOSHI", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsDangerous(string process, string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return false;
        var p = process.ToLowerInvariant();
        var c = cmd.ToLowerInvariant();
        if (!TargetsKoshi(c)) return false;

        return p switch
        {
            "sc" => ScMutate.IsMatch(c) || c.Contains(" delete ", StringComparison.Ordinal),
            "net" => c.Contains(" stop", StringComparison.Ordinal),
            "taskkill" => true,
            "msiexec" => c.Contains("/x", StringComparison.Ordinal) || c.Contains("/uninstall", StringComparison.Ordinal),
            "takeown" or "icacls" or "reg" or "regedit" or "regedt32" or "schtasks" => true,
            "wmic" => c.Contains("delete", StringComparison.Ordinal) || c.Contains("stop", StringComparison.Ordinal) || c.Contains("uninstall", StringComparison.Ordinal),
            "control" or "rundll32" => c.Contains("appwiz", StringComparison.Ordinal) || c.Contains("uninstall", StringComparison.Ordinal),
            _ => MutationMarks.Any(m => c.Contains(m, StringComparison.Ordinal)),
        };
    }

    private static bool TargetsKoshi(string cmd) =>
        cmd.Contains("koshi", StringComparison.OrdinalIgnoreCase)
        || cmd.Contains("koshiagentservice", StringComparison.OrdinalIgnoreCase)
        || cmd.Contains("koshiagenthealth", StringComparison.OrdinalIgnoreCase)
        || cmd.Contains("koshihealth", StringComparison.OrdinalIgnoreCase)
        || (cmd.Contains("image file execution", StringComparison.OrdinalIgnoreCase) && cmd.Contains("debugger", StringComparison.OrdinalIgnoreCase))
        || (cmd.Contains("ifeo", StringComparison.OrdinalIgnoreCase) && cmd.Contains("debugger", StringComparison.OrdinalIgnoreCase))
        || (cmd.Contains("debugger", StringComparison.OrdinalIgnoreCase) && cmd.Contains("systray", StringComparison.OrdinalIgnoreCase))
        || (cmd.Contains("set-itemproperty", StringComparison.OrdinalIgnoreCase) && cmd.Contains("debugger", StringComparison.OrdinalIgnoreCase))
        || (cmd.Contains("new-itemproperty", StringComparison.OrdinalIgnoreCase) && cmd.Contains("debugger", StringComparison.OrdinalIgnoreCase));

    private void KillHijackWriters()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName.ToLowerInvariant();
                if (name is not ("powershell" or "pwsh" or "powershell_ise" or "cmd" or "reg" or "regedit" or "regedt32" or "python" or "pythonw"))
                    continue;
                var cmd = Native.CommandLine(p.Id);
                if (SelfHeal.IsHealProcess(cmd)) continue;
                logger.LogWarning("Blocked IFEO hijack writer: {Name} {Cmd}", p.ProcessName, Truncate(cmd));
                KillProcess(p);
            }
            catch { /* already gone */ }
            finally { p.Dispose(); }
        }
    }

    private void ProtectIfeoKeys()
    {
        foreach (var hive in new[]
                 {
                     @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                 })
        {
            foreach (var image in SelfHeal.ProtectedImages)
            {
                try
                {
                    Native.SetObjectSddl($@"MACHINE\{hive}\{image}", Native.SeRegistryKey, HardenedRegistrySddl);
                }
                catch { /* key may not exist yet */ }
            }
        }
    }

    private static bool IsTrustedSetup(Process p)
    {
        var name = p.ProcessName;
        var path = Native.ImagePath(p.Id);
        if (AgentSelfUpdate.IsSetup(name, path)) return true;
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
                if (AgentSelfUpdate.IsSetup(p.ProcessName, Native.ImagePath(p.Id))) return true;
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

    /// <summary>Older builds left an Inno uninstaller in ARP. Remove it so Windows cannot offer "Uninstall".</summary>
    private static void RemoveLeftoverUninstaller()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6B0C2A0E-5C1E-4C8E-9B54-6F1F0B9E2A11}_is1", throwOnMissingSubKey: false);
            Registry.LocalMachine.DeleteSubKeyTree(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{6B0C2A0E-5C1E-4C8E-9B54-6F1F0B9E2A11}_is1", throwOnMissingSubKey: false);
        }
        catch (UnauthorizedAccessException) { }

        try
        {
            var installRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            if (!Directory.Exists(installRoot)) return;
            foreach (var f in Directory.EnumerateFiles(installRoot, "unins*.*"))
            {
                try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
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

        [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool QueryFullProcessImageName(nint handle, uint flags, [Out] char[] buffer, ref uint size);

        public static string? ImagePath(int pid)
        {
            string? path = null;
            WithHandle(pid, h =>
            {
                var buffer = new char[1024];
                var size = (uint)buffer.Length;
                if (QueryFullProcessImageName(h, 0, buffer, ref size)) path = new string(buffer, 0, (int)size);
            });
            return path;
        }

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

    private static int _uninstalling;

    public static bool IsUninstalling => Volatile.Read(ref _uninstalling) != 0;

    public static bool IsOpen =>
        IsUninstalling
        || DateTime.UtcNow.Ticks < Interlocked.Read(ref _untilTicks)
        || Volatile.Read(ref _setupSeen) != 0;

    public static void Enter(TimeSpan duration)
    {
        var until = DateTime.UtcNow.Add(duration).Ticks;
        long current;
        do { current = Interlocked.Read(ref _untilTicks); }
        while (until > current && Interlocked.CompareExchange(ref _untilTicks, until, current) != current);
    }

    public static void MarkSetup(bool seen) => Volatile.Write(ref _setupSeen, seen ? 1 : 0);

    /// <summary>Admin-initiated remote uninstall: UninstallGuard stops intercepting so the remover script can run.</summary>
    public static void AllowUninstall()
    {
        Volatile.Write(ref _uninstalling, 1);
        Enter(TimeSpan.FromMinutes(15));
    }
}
