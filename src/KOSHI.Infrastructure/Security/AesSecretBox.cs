using System.Security.Cryptography;
using System.Text;
using KOSHI.Application.Abstractions;

namespace KOSHI.Infrastructure.Security;

/// <summary>AES-GCM using a key derived from the JWT signing secret. Values are stored as v1.&lt;base64&gt;.</summary>
public sealed class AesSecretBox(JwtOptions jwt) : ISecretBox
{
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes(jwt.SigningKey));

    public string Seal(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        var packed = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, packed, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length + cipher.Length, tag.Length);
        return "v1." + Convert.ToBase64String(packed);
    }

    public string? Open(string? sealedValue)
    {
        if (string.IsNullOrWhiteSpace(sealedValue) || !sealedValue.StartsWith("v1.", StringComparison.Ordinal)) return null;
        try
        {
            var packed = Convert.FromBase64String(sealedValue[3..]);
            if (packed.Length < 28) return null;
            var nonce = packed.AsSpan(0, 12);
            var tag = packed.AsSpan(packed.Length - 16, 16);
            var cipher = packed.AsSpan(12, packed.Length - 28);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }
}
