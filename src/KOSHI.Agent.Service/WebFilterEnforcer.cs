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
        @"SOFTWARE\Policies\Opera Software\Opera",
        @"SOFTWARE\Policies\Opera Software\Opera GX",
    ];
    private const string FirefoxRoot = @"SOFTWARE\Policies\Mozilla\Firefox";

    /// <summary>When a well-known site is blocked, also block its CDNs so a timer expiry actually stops video/feed playback.</summary>
    private static readonly Dictionary<string, string[]> Companions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube.com"] = ["youtu.be", "googlevideo.com", "ytimg.com", "youtube-nocookie.com", "ggpht.com", "yt3.ggpht.com"],
        ["youtu.be"] = ["youtube.com", "googlevideo.com", "ytimg.com", "youtube-nocookie.com"],
        ["instagram.com"] = ["cdninstagram.com", "fbcdn.net"],
        ["facebook.com"] = ["fbcdn.net", "facebook.net"],
        ["tiktok.com"] = ["tiktokcdn.com", "tiktokv.com", "musical.ly"],
        ["x.com"] = ["twitter.com", "twimg.com"],
        ["twitter.com"] = ["x.com", "twimg.com"],
        ["netflix.com"] = ["nflxvideo.net", "nflximg.net"],
        ["twitch.tv"] = ["jtvnw.net", "ttvnw.net"],
    };

    private string? _lastSignature;
    private HashSet<string> _lastDomains = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="allowedDomains">Sites with an active timed permission; they and their CDNs win over companions of other blocked sites.</param>
    public void Apply(IReadOnlyCollection<string> blockedDomains, IReadOnlyCollection<string> allowedDomains, bool forceDirectConnection)
    {
        var allowed = Expand(allowedDomains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var domains = Expand(blockedDomains).Where(d => !allowed.Contains(d)).ToList();
        var signature = $"{string.Join('|', domains)}#{forceDirectConnection}";
        if (signature == _lastSignature) return;

        var newlyBlocked = domains.Any(d => !_lastDomains.Contains(d));
        var ok = TryStep("hosts", () => WriteHosts(domains));
        ok &= TryStep("chromium", () => WriteChromium(domains, forceDirectConnection));
        ok &= TryStep("firefox", () => WriteFirefox(domains, forceDirectConnection));
        TryStep("flushdns", FlushDns);
        // Chromium browsers only re-read registry policies on a Group Policy notification.
        TryStep("gprefresh", BrowserNet.RefreshPolicies);
        if (newlyBlocked) TryStep("reset", () => BrowserNet.ResetBrowserConnections(logger));
        if (ok)
        {
            _lastSignature = signature;
            _lastDomains = domains.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        logger.LogInformation("Web filter applied: {Count} blocked domains, {Allowed} allowed, direct connection forced: {Direct}", domains.Count, allowed.Count, forceDirectConnection);
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
                var n = 1;
                foreach (var d in domains)
                {
                    foreach (var pattern in ChromiumPatterns(d))
                        list.SetValue((n++).ToString(System.Globalization.CultureInfo.InvariantCulture), pattern, RegistryValueKind.String);
                }
                key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
                // QUIC sessions survive TCP resets; TCP-only lets a new block cut open tabs at once.
                key.SetValue("QuicAllowed", 0, RegistryValueKind.DWord);
            }
            else
            {
                key.DeleteValue("DnsOverHttpsMode", throwOnMissingValue: false);
                key.DeleteValue("QuicAllowed", throwOnMissingValue: false);
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

    private static List<string> Expand(IReadOnlyCollection<string> blocked)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in blocked)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var host = raw.Trim().TrimStart('*', '.').ToLowerInvariant();
            if (host.Length == 0) continue;
            set.Add(host);
            if (Companions.TryGetValue(host, out var extra))
                foreach (var e in extra) set.Add(e);
        }
        return set.OrderBy(d => d, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> ChromiumPatterns(string domain)
    {
        yield return domain;
        yield return $"*.{domain}";
        yield return $"www.{domain}";
        yield return $"https://{domain}";
        yield return $"https://*.{domain}";
        yield return $"http://{domain}";
        yield return $"http://*.{domain}";
    }
}
