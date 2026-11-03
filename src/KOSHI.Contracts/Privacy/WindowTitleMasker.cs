using System.Text.RegularExpressions;

namespace KOSHI.Contracts.Privacy;

/// <summary>
/// Shared by agent and server so that "Masked" means the same thing on both sides.
/// Removes e-mail addresses, URLs (keeps host), long digit sequences (phones, card/account numbers)
/// and file-system paths, then truncates.
/// </summary>
public static partial class WindowTitleMasker
{
    public const int MaskedMaxLength = 128;

    public static string? Apply(string? title, WindowTitleCaptureDto mode)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        return mode switch
        {
            WindowTitleCaptureDto.None => null,
            WindowTitleCaptureDto.Masked => Mask(title),
            _ => Truncate(title.Trim(), AgentLimits.MaxWindowTitleLength),
        };
    }

    public static string Mask(string title)
    {
        var s = title.Trim();
        s = EmailRegex().Replace(s, "[email]");
        s = UrlRegex().Replace(s, m => m.Groups["host"].Value);
        s = PathRegex().Replace(s, "[path]");
        s = DigitsRegex().Replace(s, "[number]");
        s = WhitespaceRegex().Replace(s, " ");
        return Truncate(s, MaskedMaxLength);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, 200)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:https?|ftp)://(?<host>[^/\s:?#]+)[^\s]*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 200)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\)[^\s""<>|]*", RegexOptions.CultureInvariant, 200)]
    private static partial Regex PathRegex();

    [GeneratedRegex(@"\+?\d[\d\s\-]{3,}\d", RegexOptions.CultureInvariant, 200)]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant, 200)]
    private static partial Regex WhitespaceRegex();
}
