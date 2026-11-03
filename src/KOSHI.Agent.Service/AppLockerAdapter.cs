using System.Diagnostics;
using System.Security;
using System.Text;
using KOSHI.Agent.Core;
using KOSHI.Contracts;
using Microsoft.Win32;

namespace KOSHI.Agent.Service;

public sealed record AdapterResult(PolicyExecutionStatusDto Status, string Adapter, string Message);

public static class WindowsEdition
{
    public static string EditionId => Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "") as string ?? "";

    public static string Describe()
    {
        var product = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "Windows") as string ?? "Windows";
        var build = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", "") as string;
        return $"{product} ({EditionId}) build {build}";
    }

    /// <summary>AppLocker rules are enforced only on Enterprise, Education and Server editions.</summary>
    public static bool SupportsAppLockerEnforcement =>
        EditionId.StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase) ||
        EditionId.StartsWith("Education", StringComparison.OrdinalIgnoreCase) ||
        EditionId.StartsWith("Server", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Applies "Block" application rules through Windows AppLocker. A backup of the machine's previous local
/// AppLocker policy is taken before the first change and restored by <see cref="Rollback"/>.
/// The generated policy always contains the Windows default allow rules so Windows and Program Files keep working.
/// </summary>
public static class AppLockerAdapter
{
    private static string BackupFile => Path.Combine(AgentPaths.MachineDir, "applocker-backup.xml");
    private static string PolicyFile => Path.Combine(AgentPaths.MachineDir, "applocker-koshi.xml");

    public static AdapterResult Apply(AgentPolicyDto policy)
    {
        if (policy.Mode == PolicyModeDto.AuditOnly)
            return new(PolicyExecutionStatusDto.Applied, "AuditOnly", "Audit-only rejim: taqiqlangan dasturlar bloklanmaydi, faqat qayd etiladi.");
        if (!policy.EnforcementApproved)
            return new(PolicyExecutionStatusDto.RequiresAdministratorApproval, "AppLocker", "Enforcement administrator tomonidan tasdiqlanmagan.");

        var blocked = policy.Rules.Where(r => r.Restriction == ApplicationRestrictionDto.Block).ToList();
        if (blocked.Count == 0) return Rollback() with { Message = "Bloklanadigan qoidalar yo'q; oldingi AppLocker siyosati tiklandi." };
        if (!WindowsEdition.SupportsAppLockerEnforcement)
            return new(PolicyExecutionStatusDto.Unsupported, "AppLocker",
                $"Windows nashri '{WindowsEdition.EditionId}' AppLocker enforcement'ni qo'llab-quvvatlamaydi (Enterprise/Education kerak). Qoidalar faqat audit qilinadi.");

        try
        {
            if (!File.Exists(BackupFile))
            {
                var (ok, output) = PowerShell("Get-AppLockerPolicy -Local -Xml");
                if (!ok) return new(PolicyExecutionStatusDto.Failed, "AppLocker", $"Oldingi siyosat zaxirasini olib bo'lmadi: {output}");
                File.WriteAllText(BackupFile, output);
            }

            File.WriteAllText(PolicyFile, BuildPolicyXml(blocked), Encoding.UTF8);
            Run("sc.exe", "config AppIDSvc start= auto");
            Run("sc.exe", "start AppIDSvc");
            var (applied, message) = PowerShell($"Set-AppLockerPolicy -XmlPolicy '{PolicyFile}'");
            return applied
                ? new(PolicyExecutionStatusDto.Applied, "AppLocker", $"{blocked.Count} ta dastur AppLocker orqali bloklandi.")
                : new(PolicyExecutionStatusDto.Failed, "AppLocker", Truncate(message));
        }
        catch (Exception ex)
        {
            return new(PolicyExecutionStatusDto.Failed, "AppLocker", Truncate(ex.Message));
        }
    }

    /// <summary>Emergency rollback: restores the AppLocker policy that existed before KOSHI changed it.</summary>
    public static AdapterResult Rollback()
    {
        if (!File.Exists(BackupFile)) return new(PolicyExecutionStatusDto.Applied, "AppLocker", "KOSHI AppLocker siyosatini o'zgartirmagan.");
        var (ok, message) = PowerShell($"Set-AppLockerPolicy -XmlPolicy '{BackupFile}'");
        if (ok) File.Delete(BackupFile);
        return ok ? new(PolicyExecutionStatusDto.Applied, "AppLocker", "Oldingi AppLocker siyosati tiklandi.") : new(PolicyExecutionStatusDto.Failed, "AppLocker", Truncate(message));
    }

    public static string BuildPolicyXml(IEnumerable<AgentApplicationRuleDto> blocked)
    {
        static string E(string s) => SecurityElement.Escape(s);
        var sb = new StringBuilder();
        sb.AppendLine("<AppLockerPolicy Version=\"1\">");
        sb.AppendLine("  <RuleCollection Type=\"Exe\" EnforcementMode=\"Enabled\">");
        sb.AppendLine("    <FilePathRule Id=\"921cc481-6e17-4653-8f75-050b80acca20\" Name=\"(Default) Program Files\" Description=\"\" UserOrGroupSid=\"S-1-1-0\" Action=\"Allow\"><Conditions><FilePathCondition Path=\"%PROGRAMFILES%\\*\" /></Conditions></FilePathRule>");
        sb.AppendLine("    <FilePathRule Id=\"a61c8b2c-a319-4cd0-9690-d2177cad7b51\" Name=\"(Default) Windows\" Description=\"\" UserOrGroupSid=\"S-1-1-0\" Action=\"Allow\"><Conditions><FilePathCondition Path=\"%WINDIR%\\*\" /></Conditions></FilePathRule>");
        sb.AppendLine("    <FilePathRule Id=\"fd686d83-a829-4351-8ff4-27c7de5755d2\" Name=\"(Default) Administrators\" Description=\"\" UserOrGroupSid=\"S-1-5-32-544\" Action=\"Allow\"><Conditions><FilePathCondition Path=\"*\" /></Conditions></FilePathRule>");
        sb.AppendLine("    <FilePathRule Id=\"5b2e1a37-1f0a-4c1e-9a7b-6f3f0f6c1a01\" Name=\"KOSHI: user profile apps\" Description=\"Allow per-user installed apps\" UserOrGroupSid=\"S-1-1-0\" Action=\"Allow\"><Conditions><FilePathCondition Path=\"%OSDRIVE%\\Users\\*\" /></Conditions></FilePathRule>");
        foreach (var r in blocked)
        {
            var id = r.Id == Guid.Empty ? Guid.NewGuid() : r.Id;
            var name = E($"KOSHI block: {r.Name}");
            if (!string.IsNullOrWhiteSpace(r.Publisher))
                sb.AppendLine($"    <FilePublisherRule Id=\"{id}\" Name=\"{name}\" Description=\"\" UserOrGroupSid=\"S-1-1-0\" Action=\"Deny\"><Conditions><FilePublisherCondition PublisherName=\"{E(r.Publisher!)}\" ProductName=\"*\" BinaryName=\"{E(r.ProcessName.ToUpperInvariant())}\"><BinaryVersionRange LowSection=\"*\" HighSection=\"*\" /></FilePublisherCondition></Conditions></FilePublisherRule>");
            else
                sb.AppendLine($"    <FilePathRule Id=\"{id}\" Name=\"{name}\" Description=\"\" UserOrGroupSid=\"S-1-1-0\" Action=\"Deny\"><Conditions><FilePathCondition Path=\"{E(r.PathPattern ?? $"*\\{r.ProcessName}")}\" /></Conditions></FilePathRule>");
        }
        sb.AppendLine("  </RuleCollection>");
        sb.AppendLine("</AppLockerPolicy>");
        return sb.ToString();
    }

    private static (bool Ok, string Output) PowerShell(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        return (p.ExitCode == 0 && string.IsNullOrWhiteSpace(stderr), string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    private static void Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true })!;
        p.WaitForExit(30_000);
    }

    private static string Truncate(string s) => s.Length <= 900 ? s.Trim() : s[..900];
}
