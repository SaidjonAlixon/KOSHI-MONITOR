using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace KOSHI.Agent.Service;

/// <summary>
/// Employees must not turn off Wi-Fi or airplane mode. Wireless adapters that get disabled are turned
/// back on; commands that disconnect or disable them are killed.
/// </summary>
internal sealed class NetworkAdapterGuard(ILogger logger)
{
    public void Tick()
    {
        try
        {
            ClearAirplaneMode();
            ReenableWireless();
            KillDisableCommands();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Network adapter guard tick failed");
        }
    }

    private static void ClearAirplaneMode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement", writable: true);
            key?.SetValue("SystemRadioState", 0, RegistryValueKind.DWord);
        }
        catch (UnauthorizedAccessException) { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Radio", writable: true);
            key?.SetValue("AirplaneModeEnabled", 0, RegistryValueKind.DWord);
        }
        catch (UnauthorizedAccessException) { }
    }

    private void ReenableWireless()
    {
        Run("sc.exe", "config WlanSvc start= auto");
        Run("sc.exe", "start WlanSvc");
        Run("netsh.exe", "wlan set autoconfig enabled=yes");
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
            if (nic.OperationalStatus is OperationalStatus.Up or OperationalStatus.Dormant) continue;
            logger.LogWarning("Re-enabling wireless adapter {Name}", nic.Name);
            Run("netsh.exe", $"interface set interface name=\"{nic.Name}\" admin=enabled");
        }
    }

    private static void KillDisableCommands()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName.ToLowerInvariant();
                if (name is not ("netsh" or "powershell" or "pwsh" or "powershell_ise" or "cmd" or "wmic"))
                    continue;
                var cmd = UninstallGuard.ReadCommandLine(p.Id)?.ToLowerInvariant() ?? "";
                if (cmd.Length == 0) continue;
                if (IsHeal(cmd)) continue;
                if (!IsWifiDisable(cmd)) continue;
                p.Kill(entireProcessTree: true);
            }
            catch { /* already gone */ }
            finally { p.Dispose(); }
        }
    }

    private static bool IsHeal(string cmd) => SelfHeal.IsHealProcess(cmd);

    private static bool IsWifiDisable(string cmd)
    {
        if (cmd.Contains("disable-netadapter", StringComparison.Ordinal)) return true;
        if (cmd.Contains("disable-pnpdevice", StringComparison.Ordinal)) return true;
        if (cmd.Contains("wlan disconnect", StringComparison.Ordinal)) return true;
        if (cmd.Contains("wlan set blockednetworks", StringComparison.Ordinal)) return true;
        if (cmd.Contains("autoconfig", StringComparison.Ordinal) && cmd.Contains("enabled=no", StringComparison.Ordinal)) return true;
        if (cmd.Contains("set-netadapter", StringComparison.Ordinal) && cmd.Contains("disabled", StringComparison.Ordinal)) return true;
        if (cmd.Contains("airplane", StringComparison.Ordinal) && (cmd.Contains("radio", StringComparison.Ordinal) || cmd.Contains("ms-settings", StringComparison.Ordinal))) return true;
        if (cmd.Contains("interface set interface", StringComparison.Ordinal) && cmd.Contains("disabled", StringComparison.Ordinal)
            && !cmd.Contains("tap", StringComparison.Ordinal) && !cmd.Contains("wintun", StringComparison.Ordinal)
            && !cmd.Contains("wireguard", StringComparison.Ordinal) && !cmd.Contains("nordlynx", StringComparison.Ordinal))
            return true;
        if (cmd.Contains("radiomanagement", StringComparison.Ordinal) && cmd.Contains("airplane", StringComparison.Ordinal)) return true;
        return false;
    }

    private static void Run(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                CreateNoWindow = true, UseShellExecute = false,
            });
            p?.WaitForExit(4000);
        }
        catch { /* netsh missing */ }
    }
}
