using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace KOSHI.Agent.Service;

/// <summary>
/// Blocks websites in every browser:
/// 1) Browser enterprise policies (URLBlocklist) for Chrome, Edge, Brave, Yandex, Chromium and Firefox (WebsiteFilter) —
///    covers subdomains, incognito and DNS-over-HTTPS; the browser shows "blocked by administrator".
/// 2) A managed section in the Windows hosts file for any other browser or app.
/// 3) Browser DNS-over-HTTPS is turned off so the hosts entries cannot be bypassed.
/// When VPN blocking is on, browsers are also forced to a direct connection so proxy-based "VPN" extensions stop working.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WebFilterEnforcer(ILogger logger)
{
    private const string HostsBegin = "# >>> KOSHI web filter — administrator tomonidan boshqariladi";
    private const string HostsEnd = "# <<< KOSHI web filter";
    private static readonly string HostsPath = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

    private static readonly string[] ChromiumRoots =
    [
        @"SOFTWARE\Policies\Google\Chrome",
        @"SOFTWARE\Policies\Microsoft\Edge",
        @"SOFTWARE\Policies\BraveSoftware\Brave",
        @"SOFTWARE\Policies\YandexBrowser",
        @"SOFTWARE\Policies\Chromium",
    ];
    private const string FirefoxRoot = @"SOFTWARE\Policies\Mozilla\Firefox";

    private string? _lastSignature;

    public void Apply(IReadOnlyCollection<string> blockedDomains, bool forceDirectConnection)
    {
        var domains = blockedDomains.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d, StringComparer.Ordinal).ToList();
        var signature = $"{string.Join('|', domains)}#{forceDirectConnection}";
        if (signature == _lastSignature) return;

        var ok = TryStep("hosts", () => WriteHosts(domains));
        ok &= TryStep("chromium", () => WriteChromium(domains, forceDirectConnection));
        ok &= TryStep("firefox", () => WriteFirefox(domains, forceDirectConnection));
        TryStep("flushdns", FlushDns);
        if (ok) _lastSignature = signature;
        logger.LogInformation("Web filter applied: {Count} blocked domains, direct connection forced: {Direct}", domains.Count, forceDirectConnection);
    }

    private bool TryStep(string name, Action step)
    {
        try
        {
            step();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Web filter step {Step} failed", name);
            return false;
        }
    }

    private static void WriteHosts(IReadOnlyList<string> domains)
    {
        var existing = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : string.Empty;
        var kept = new StringBuilder();
        var inSection = false;
        foreach (var line in existing.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith(HostsBegin, StringComparison.Ordinal)) { inSection = true; continue; }
            if (line.StartsWith(HostsEnd, StringComparison.Ordinal)) { inSection = false; continue; }
            if (!inSection) kept.Append(line).Append("\r\n");
        }
        var text = kept.ToString().TrimEnd('\r', '\n') + "\r\n";
        if (domains.Count > 0)
        {
            var section = new StringBuilder().Append("\r\n").Append(HostsBegin).Append("\r\n");
            foreach (var d in domains)
            {
                section.Append("0.0.0.0 ").Append(d).Append("\r\n");
                section.Append("0.0.0.0 www.").Append(d).Append("\r\n");
                section.Append(":: ").Append(d).Append("\r\n");
                section.Append(":: www.").Append(d).Append("\r\n");
            }
            section.Append(HostsEnd).Append("\r\n");
            text += section.ToString();
        }
        if (text == existing) return;

        var info = new FileInfo(HostsPath);
        if (info.Exists && info.IsReadOnly) info.IsReadOnly = false;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(HostsPath, text, Encoding.ASCII);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static void WriteChromium(IReadOnlyList<string> domains, bool direct)
    {
        var nothingToWrite = domains.Count == 0 && !direct;
        foreach (var root in ChromiumRoots)
        {
            using var key = nothingToWrite ? Registry.LocalMachine.OpenSubKey(root, writable: true) : Registry.LocalMachine.CreateSubKey(root, writable: true);
            if (key is null) continue;
            key.DeleteSubKeyTree("URLBlocklist", throwOnMissingSubKey: false);
            if (domains.Count > 0)
            {
                using var list = key.CreateSubKey("URLBlocklist", writable: true);
                for (var i = 0; i < domains.Count; i++) list.SetValue((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), domains[i], RegistryValueKind.String);
                key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue("DnsOverHttpsMode", throwOnMissingValue: false);
            }

            if (direct) key.SetValue("ProxyMode", "direct", RegistryValueKind.String);
            else key.DeleteValue("ProxyMode", throwOnMissingValue: false);
        }
    }

    private static void WriteFirefox(IReadOnlyList<string> domains, bool direct)
    {
        using var root = domains.Count == 0 && !direct
            ? Registry.LocalMachine.OpenSubKey(FirefoxRoot, writable: true)
            : Registry.LocalMachine.CreateSubKey(FirefoxRoot, writable: true);
        if (root is null) return;
        root.DeleteSubKeyTree("WebsiteFilter", throwOnMissingSubKey: false);
        root.DeleteSubKeyTree("DNSOverHTTPS", throwOnMissingSubKey: false);
        if (domains.Count > 0)
        {
            using var block = root.CreateSubKey(@"WebsiteFilter\Block", writable: true);
            var n = 1;
            foreach (var d in domains)
            {
                block.SetValue((n++).ToString(System.Globalization.CultureInfo.InvariantCulture), $"*://{d}/*", RegistryValueKind.String);
                block.SetValue((n++).ToString(System.Globalization.CultureInfo.InvariantCulture), $"*://*.{d}/*", RegistryValueKind.String);
            }
            using var doh = root.CreateSubKey("DNSOverHTTPS", writable: true);
            doh.SetValue("Enabled", 0, RegistryValueKind.DWord);
            doh.SetValue("Locked", 1, RegistryValueKind.DWord);
        }

        root.DeleteSubKeyTree("Proxy", throwOnMissingSubKey: false);
        if (direct)
        {
            using var proxy = root.CreateSubKey("Proxy", writable: true);
            proxy.SetValue("Mode", "none", RegistryValueKind.String);
            proxy.SetValue("Locked", 1, RegistryValueKind.DWord);
        }
    }

    private static void FlushDns()
    {
        using var p = Process.Start(new ProcessStartInfo("ipconfig.exe", "/flushdns") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        p?.WaitForExit(5000);
    }
}
