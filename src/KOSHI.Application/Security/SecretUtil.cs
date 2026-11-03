using System.Security.Cryptography;
using System.Text;

namespace KOSHI.Application.Security;

public static class SecretUtil
{
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
    public const string DeviceCredentialPrefix = "kd1";

    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool FixedTimeEqualsHex(string a, string b) =>
        a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    public static string RandomToken(int bytes = 32) =>
        Base64Url(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>12 symbols from a 30-char alphabet (~59 bits), formatted XXXX-XXXX-XXXX. Short-lived and single-use.</summary>
    public static string NewActivationCode()
    {
        Span<char> chars = stackalloc char[12];
        for (var i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        var s = new string(chars);
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }

    public static string NormalizeActivationCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string FormatDeviceCredential(Guid credentialId, string secret) =>
        $"{DeviceCredentialPrefix}.{credentialId:N}.{secret}";

    public static bool TryParseDeviceCredential(string? value, out Guid credentialId, out string secret)
    {
        credentialId = Guid.Empty;
        secret = string.Empty;
        if (string.IsNullOrEmpty(value) || value.Length > 200) return false;
        var parts = value.Split('.');
        if (parts.Length != 3 || parts[0] != DeviceCredentialPrefix) return false;
        if (!Guid.TryParseExact(parts[1], "N", out credentialId)) return false;
        if (parts[2].Length < 32) return false;
        secret = parts[2];
        return true;
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
