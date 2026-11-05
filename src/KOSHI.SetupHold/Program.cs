using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Runs as SYSTEM during upgrade. Old 1.0.26 UninstallGuard leaves Administrators with RX-only
/// ACLs and restarts the service every second, so Inno cannot replace files. This process is
/// not a watched host (no "koshi" in the name) and:
/// 1) grants SYSTEM+Administrators full control on the install and data folders
/// 2) keeps KOSHI.Agent / KOSHI.Agent.Service dead until the installer writes upgrade.done
/// 3) overwrites heal.cmd so the scheduled task cannot sc start during copy
/// </summary>
internal static class Program
{
    private const string ServiceName = "KOSHIAgentService";
    private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KOSHI");
    private static readonly string DoneFile = Path.Combine(DataDir, "upgrade.done");
    private static readonly string LockFile = Path.Combine(DataDir, "upgrade.lock");
    private static readonly string HealFile = Path.Combine(DataDir, "heal.cmd");
    private static readonly string InstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KOSHI");

    [STAThread]
    private static void Main()
    {
        try
        {
            EnableDebugPrivilege();
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(LockFile, DateTime.UtcNow.ToString("o"));
            File.WriteAllText(HealFile, "@echo off\r\nexit /b 0\r\n");
            GrantFull(InstallRoot);
            GrantFull(DataDir);
            ClearFailureActions();

            var deadline = DateTime.UtcNow.AddMinutes(4);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(DoneFile)) break;
                File.WriteAllText(LockFile, DateTime.UtcNow.ToString("o"));
                try { File.WriteAllText(HealFile, "@echo off\r\nexit /b 0\r\n"); } catch { /* RX race */ }
                KillImage("KOSHI.Agent");
                KillImage("KOSHI.Agent.Service");
                Thread.Sleep(120);
            }
        }
        catch
        {
            // Installer still retries; never surface a window.
        }
    }

    private static void KillImage(string name)
    {
        Process[] found;
        try { found = Process.GetProcessesByName(name); }
        catch { return; }
        // Never kill the tree: the running installer is a child of the service or tray that started it.
        foreach (var p in found)
        {
            try { p.Kill(); }
            catch { /* gone */ }
            finally { p.Dispose(); }
        }
    }

    private static void ClearFailureActions()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", $"failure {ServiceName} reset= 0 actions= \"\"")
            {
                CreateNoWindow = true, UseShellExecute = false,
            });
            p?.WaitForExit(4000);
        }
        catch { /* SDDL may deny; kill-loop still covers it */ }
    }

    private static void GrantFull(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return;
        // SYSTEM + Administrators full control, Users read/execute so shortcuts keep working if setup is interrupted.
        SetSddl(path, "D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)");
    }

    private static void SetSddl(string path, string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var sd, out _))
            return;
        try
        {
            if (!GetSecurityDescriptorDacl(sd, out _, out var dacl, out _)) return;
            _ = SetNamedSecurityInfo(path, 1, 0x00000004 | 0x80000000, IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero);
        }
        finally
        {
            LocalFree(sd);
        }
    }

    private static void EnableDebugPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var token)) return;
        try
        {
            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid)) return;
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 2 };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID Luid;
        public int Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr sd, out uint size);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorDacl(IntPtr sd, out bool present, out IntPtr dacl, out bool defaulted);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint SetNamedSecurityInfo(string name, uint objectType, uint securityInfo, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr h);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disable, ref TOKEN_PRIVILEGES newState, int length, IntPtr prev, IntPtr ret);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
