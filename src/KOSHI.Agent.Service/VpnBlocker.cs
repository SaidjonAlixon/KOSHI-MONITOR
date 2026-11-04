using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace KOSHI.Agent.Service;

/// <summary>Known consumer VPN / proxy-tunnel software. Enterprise VPN clients (FortiClient, AnyConnect, GlobalProtect) are intentionally not listed.</summary>
public static class VpnCatalog
{
    public static readonly IReadOnlySet<string> ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "openvpn", "openvpn-gui", "openvpnserv", "openvpnserv2", "openvpnconnect", "ovpnconnector",
        "wireguard", "wg", "nordvpn", "nordvpn-service", "nordsec-threatprotection-service",
        "expressvpn", "expressvpnd", "expressvpn.systemservice", "protonvpn", "protonvpn.client", "protonvpnservice", "protonvpn.wireguardservice",
        "surfshark", "surfshark.service", "windscribe", "windscribeservice", "hsscp", "hotspotshield", "hydra", "tunnelbear", "tunnelbear.maintenance",
        "cyberghost", "cyberghost.service", "pia-client", "pia-service", "pia-wgservice", "mullvad vpn", "mullvad-vpn", "mullvad-daemon",
        "ipvanish", "ipvanish.service", "psiphon3", "psiphon", "psiphon-tunnel-core", "warp-svc", "cloudflare warp", "outline", "outline-ss-local",
        "v2rayn", "v2ray", "xray", "clash", "clash-verge", "clash for windows", "clash-meta", "mihomo", "nekoray", "nekobox", "hiddify", "hiddifycli",
        "sing-box", "shadowsocks", "ss-local", "tor", "lantern", "ultrasurf", "u1801", "hola", "hola_svc", "betternet", "urban-vpn", "urbanvpn",
        "planetvpn", "x-vpn", "xvpn", "vpnunlimited", "kvpnservice", "hide.me", "hideme", "purevpn", "atlasvpn", "veepn", "browsec", "zenmate",
        "softether", "vpnclient", "vpnclient_x64", "vpncmgr", "vpncmgr_x64", "radmin vpn", "rvpnnetmp", "zerotier one", "zerotier_desktop_ui", "tailscale", "tailscaled",
    };

    /// <summary>Adapter descriptions of virtual VPN network cards.</summary>
    public static readonly string[] AdapterKeywords =
    [
        "TAP-Windows", "TAP-Win32", "TAP-NordVPN", "TAP-ProtonVPN", "TAP-Surfshark", "TAP-Windscribe", "TAP-PIA", "Wintun", "WireGuard", "OpenVPN",
        "NordLynx", "ProtonVPN", "Windscribe", "Surfshark", "Mullvad", "ExpressVPN", "Hotspot Shield", "Cloudflare WARP", "TunnelBear",
        "CyberGhost", "Private Internet Access", "Psiphon", "SoftEther", "Radmin VPN", "ZeroTier", "Tailscale", "sing-tun", "Clash", "Meta Tunnel", "Hiddify",
    ];

    /// <summary>VPN provider sites blocked together with VPN software (download prevention).</summary>
    public static readonly string[] Domains =
    [
        "nordvpn.com", "expressvpn.com", "protonvpn.com", "surfshark.com", "privateinternetaccess.com", "cyberghostvpn.com", "windscribe.com",
        "hotspotshield.com", "tunnelbear.com", "hide.me", "mullvad.net", "ipvanish.com", "vyprvpn.com", "purevpn.com", "atlasvpn.com", "psiphon.ca",
        "psiphon3.com", "urban-vpn.com", "betternet.co", "hola.org", "zenmate.com", "veepn.com", "xvpn.io", "planetvpn.com", "browsec.com",
        "getoutline.org", "hiddify.com", "lantern.io", "ultrasurf.us", "torproject.org", "vpnunlimited.com", "kaspersky-vpn.com", "1clickvpn.net",
        "freevpn.org", "vpnbook.com", "protonvpn.net", "windscribe.net", "v2rayn.org", "radmin-vpn.com",
    ];
}

/// <summary>
/// Stops VPN use while enabled: kills VPN apps and services, disables their services, turns off virtual tunnel adapters
/// and disconnects Windows built-in VPN connections. Everything it changed is restored when VPN blocking is turned off.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VpnBlocker(ILogger logger)
{
    private const string StateKey = @"SOFTWARE\KOSHI\VpnBlock";
    private DateTime _lastServiceScan = DateTime.MinValue;
    private bool? _lastEnabled;

    public void Tick(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (_lastEnabled != false) Restore();
                _lastEnabled = false;
                return;
            }
            _lastEnabled = true;
            KillVpnProcesses();
            if (DateTime.UtcNow - _lastServiceScan > TimeSpan.FromSeconds(30))
            {
                _lastServiceScan = DateTime.UtcNow;
                DisableVpnServices();
            }
            DisableVpnAdapters();
            DisconnectWindowsVpn();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "VPN blocker tick failed");
        }
    }

    private void KillVpnProcesses()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (VpnCatalog.ProcessNames.Contains(p.ProcessName))
                {
                    logger.LogInformation("Blocking VPN process {Name}", p.ProcessName);
                    p.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Exited or protected
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private void DisableVpnServices()
    {
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null) return;
        using var state = Registry.LocalMachine.CreateSubKey($@"{StateKey}\Services", writable: true);
        foreach (var name in services.GetSubKeyNames())
        {
            using var svc = services.OpenSubKey(name, writable: true);
            if (svc?.GetValue("ImagePath") is not string imagePath) continue;
            var exe = ExeName(imagePath);
            if (exe is null || !VpnCatalog.ProcessNames.Contains(exe)) continue;
            if (svc.GetValue("Start") is int start && start != 4)
            {
                state.SetValue(name, start, RegistryValueKind.DWord);
                svc.SetValue("Start", 4, RegistryValueKind.DWord);
                logger.LogInformation("Disabled VPN service {Service}", name);
            }
            RunHidden("sc.exe", $"stop \"{name}\"");
        }
    }

    private void DisableVpnAdapters()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Ppp) continue;
            if (!VpnCatalog.AdapterKeywords.Any(k => nic.Description.Contains(k, StringComparison.OrdinalIgnoreCase) || nic.Name.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
            using var state = Registry.LocalMachine.CreateSubKey($@"{StateKey}\Adapters", writable: true);
            state.SetValue(nic.Name, nic.Description, RegistryValueKind.String);
            RunHidden("netsh.exe", $"interface set interface name=\"{nic.Name}\" admin=disabled");
            logger.LogInformation("Disabled VPN adapter {Adapter} ({Description})", nic.Name, nic.Description);
        }
    }

    /// <summary>Disconnects Windows VPN connections (PPTP/L2TP/SSTP/IKEv2). Broadband PPPoE connections are left alone.</summary>
    private void DisconnectWindowsVpn()
    {
        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Ppp && n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.Name).ToList();
        if (up.Count == 0) return;
        var vpnEntries = WindowsVpnEntryNames();
        foreach (var name in up.Where(vpnEntries.Contains))
        {
            RunHidden("rasdial.exe", $"\"{name}\" /disconnect");
            logger.LogInformation("Disconnected Windows VPN connection {Name}", name);
        }
    }

    /// <summary>Phonebook entries of type VPN (Type=2) from all users and the machine phonebook.</summary>
    private static HashSet<string> WindowsVpnEntryNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Network\Connections\Pbk\rasphone.pbk"),
        };
        var usersRoot = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Users");
        if (Directory.Exists(usersRoot))
        {
            foreach (var profile in Directory.EnumerateDirectories(usersRoot))
                files.Add(Path.Combine(profile, @"AppData\Roaming\Microsoft\Network\Connections\Pbk\rasphone.pbk"));
        }
        foreach (var file in files.Where(File.Exists))
        {
            try
            {
                string? entry = null;
                foreach (var raw in File.ReadLines(file))
                {
                    var line = raw.Trim();
                    if (line.StartsWith('[') && line.EndsWith(']')) entry = line[1..^1];
                    else if (entry is not null && line.Equals("Type=2", StringComparison.OrdinalIgnoreCase)) names.Add(entry);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return names;
    }

    private void Restore()
    {
        using var state = Registry.LocalMachine.OpenSubKey(StateKey, writable: true);
        if (state is null) return;
        using (var services = state.OpenSubKey("Services", writable: true))
        {
            if (services is not null)
            {
                foreach (var name in services.GetValueNames())
                {
                    using var svc = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}", writable: true);
                    if (svc is not null && services.GetValue(name) is int start) svc.SetValue("Start", start, RegistryValueKind.DWord);
                }
            }
        }
        using (var adapters = state.OpenSubKey("Adapters"))
        {
            if (adapters is not null)
            {
                foreach (var name in adapters.GetValueNames()) RunHidden("netsh.exe", $"interface set interface name=\"{name}\" admin=enabled");
            }
        }
        Registry.LocalMachine.DeleteSubKeyTree(StateKey, throwOnMissingSubKey: false);
        logger.LogInformation("VPN blocking turned off; restored VPN services and adapters");
    }

    private static string? ExeName(string imagePath)
    {
        var p = imagePath.Trim();
        if (p.StartsWith('"'))
        {
            var end = p.IndexOf('"', 1);
            p = end > 1 ? p[1..end] : p.Trim('"');
        }
        else
        {
            var exe = p.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0) p = p[..(exe + 4)];
        }
        try
        {
            return Path.GetFileNameWithoutExtension(p);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void RunHidden(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(5000);
        }
        catch
        {
            // Tool missing or failed; next tick retries.
        }
    }
}
