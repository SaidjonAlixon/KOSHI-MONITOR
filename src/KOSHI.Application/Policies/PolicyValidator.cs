using System.Text.RegularExpressions;
using KOSHI.Contracts;

namespace KOSHI.Application.Policies;

/// <summary>
/// Guards against policies that could make a computer unusable (blocking shell/system binaries,
/// over-broad path or publisher conditions) or that violate the privacy defaults.
/// </summary>
public static partial class PolicyValidator
{
    /// <summary>Windows components that must never be blocked.</summary>
    public static readonly IReadOnlySet<string> ProtectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "winlogon.exe", "csrss.exe", "lsass.exe", "services.exe", "svchost.exe", "smss.exe",
        "wininit.exe", "dwm.exe", "logonui.exe", "userinit.exe", "fontdrvhost.exe", "sihost.exe", "ctfmon.exe",
        "taskhostw.exe", "runtimebroker.exe", "searchhost.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
        "consent.exe", "msmpeng.exe", "securityhealthservice.exe", "mmc.exe", "regedit.exe", "gpupdate.exe",
        "koshi.agent.exe", "koshi.agent.service.exe",
    };

    private static readonly string[] ForbiddenPathPrefixes =
    [
        "%WINDIR%", "%SYSTEM32%", "%SYSTEMROOT%", @"C:\WINDOWS", @"\\",
    ];

    private static readonly HashSet<string> OverBroadPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "*", @"*\*", "%OSDRIVE%", @"%OSDRIVE%\*", @"C:\*", @"%PROGRAMFILES%\*", @"%PROGRAMFILES(X86)%\*",
        @"%USERPROFILE%\*", @"%LOCALAPPDATA%\*", @"%APPDATA%\*", @"%REMOVABLE%\*", @"%HOT%\*",
    };

    public static Dictionary<string, string[]> Validate(PolicySnapshot s, bool requireTargets = true)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list)) errors[key] = list = [];
            list.Add(message);
        }

        if (string.IsNullOrWhiteSpace(s.Name)) Add("Name", "Name is required.");
        if (s.SamplingIntervalSeconds is < 2 or > 5) Add("SamplingIntervalSeconds", "Sampling interval must be between 2 and 5 seconds.");
        if (s.IdleThresholdSeconds is < 60 or > 7200) Add("IdleThresholdSeconds", "Idle threshold must be between 60 and 7200 seconds.");
        if (s.HeartbeatIntervalSeconds is < 15 or > 600) Add("HeartbeatIntervalSeconds", "Heartbeat interval must be between 15 and 600 seconds.");
        if (s.EffectiveTo is { } to && to <= s.EffectiveFrom) Add("EffectiveTo", "EffectiveTo must be after EffectiveFrom.");
        if (s.ScreenshotsEnabled) Add("ScreenshotsEnabled", "Screenshots are not supported in this release and must stay disabled.");
        if (s.Mode == PolicyModeDto.Enforce && !s.EnforcementApproved)
            Add("Mode", "Enforce mode requires explicit administrator approval (approveEnforcement=true). Test the policy in AuditOnly mode first.");

        if (requireTargets && s.Assignments.Count == 0) Add("Assignments", "Policy must target at least one device, department or all devices.");
        foreach (var a in s.Assignments)
        {
            var ok = a.Scope switch
            {
                PolicyAssignmentScopeDto.AllDevices => a.DeviceId is null && a.DepartmentId is null,
                PolicyAssignmentScopeDto.Department => a.DepartmentId is not null && a.DeviceId is null,
                PolicyAssignmentScopeDto.Device => a.DeviceId is not null && a.DepartmentId is null,
                _ => false,
            };
            if (!ok) Add("Assignments", $"Assignment with scope {a.Scope} has inconsistent target ids.");
        }

        var byProcess = new Dictionary<string, AgentApplicationRuleDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in s.Rules)
        {
            var key = $"Rules[{r.ProcessName}]";
            foreach (var e in ValidateRule(r.ProcessName, r.PathPattern, r.Publisher, r.Classification, r.Restriction)) Add(key, e);

            if (byProcess.TryGetValue(r.ProcessName, out var prev) &&
                (prev.Classification != r.Classification || prev.Restriction != r.Restriction))
                Add(key, "Conflicting rules exist for the same process.");
            byProcess[r.ProcessName] = r;
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    public static IEnumerable<string> ValidateRule(string processName, string? pathPattern, string? publisher,
        ApplicationClassificationDto classification, ApplicationRestrictionDto restriction)
    {
        if (!ExeNameRegex().IsMatch(processName ?? string.Empty))
            yield return "Process name must be an executable file name such as 'app.exe' (no path, no wildcards).";

        if (restriction == ApplicationRestrictionDto.Block)
        {
            if (ProtectedProcesses.Contains(processName ?? string.Empty))
                yield return $"'{processName}' is a protected Windows/KOSHI component and cannot be blocked.";
            if (classification == ApplicationClassificationDto.Approved)
                yield return "An approved application cannot be blocked at the same time.";
        }

        if (!string.IsNullOrWhiteSpace(pathPattern))
        {
            var p = pathPattern.Trim();
            if (OverBroadPaths.Contains(p) || p.Length < 6)
                yield return "Path condition is too broad and could block large parts of the system.";
            if (ForbiddenPathPrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                yield return "Path conditions under the Windows directory or network shares are not allowed.";
            if (p.IndexOfAny(['"', '<', '>', '|']) >= 0)
                yield return "Path condition contains invalid characters.";
        }

        if (!string.IsNullOrWhiteSpace(publisher))
        {
            var pub = publisher.Trim();
            if (pub == "*" || pub.Length < 4) yield return "Publisher condition is too broad.";
            if (restriction == ApplicationRestrictionDto.Block && pub.Contains("MICROSOFT", StringComparison.OrdinalIgnoreCase))
                yield return "Blocking by the Microsoft publisher is not allowed (would affect Windows components).";
            if (pub.IndexOfAny(['"', '<', '>']) >= 0) yield return "Publisher contains invalid characters.";
        }
    }

    public static string NormalizeProcessName(string name) => name.Trim().ToLowerInvariant();

    [GeneratedRegex(@"^[^\\/:*?""<>|\s][^\\/:*?""<>|]{0,250}\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExeNameRegex();
}
