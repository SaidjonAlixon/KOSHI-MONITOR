using System.Security.Cryptography;
using System.Text;

namespace KOSHI.Contracts;

/// <summary>
/// Shared hashing for per-application unlock passwords. The server stores salt + hash in the policy;
/// the agent verifies the typed password offline. Plaintext is never stored or sent.
/// </summary>
public static class AppLockPassword
{
    public static (string Salt, string Hash) Create(string password)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        return (salt, Compute(salt, password));
    }

    public static bool Verify(string? salt, string? hash, string password)
    {
        if (string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(hash)) return false;
        var actual = Compute(salt, password);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(hash));
    }

    private static string Compute(string salt, string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{password}")));
}
