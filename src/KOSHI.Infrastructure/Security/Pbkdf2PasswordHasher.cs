using System.Globalization;
using System.Security.Cryptography;
using KOSHI.Application.Abstractions;

namespace KOSHI.Infrastructure.Security;

/// <summary>PBKDF2-HMAC-SHA256 with a per-password 128-bit salt. Format: pbkdf2-sha256$iterations$salt$hash.</summary>
public sealed class Pbkdf2PasswordHasher(int iterations = Pbkdf2PasswordHasher.DefaultIterations) : IPasswordHasher
{
    public const int DefaultIterations = 600_000;
    private const string Scheme = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
        return string.Join('$', Scheme, iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public bool Verify(string password, string encodedHash, out bool needsRehash)
    {
        needsRehash = false;
        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iter) || iter < 10_000)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        var ok = CryptographicOperations.FixedTimeEquals(actual, expected);
        needsRehash = ok && iter < iterations;
        return ok;
    }
}
