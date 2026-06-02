using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;

namespace SlackTube.Api.Services.Secrets;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
    /// <summary>Decrypt, returning null on empty input or any crypto/format failure.</summary>
    string? TryUnprotect(string? ciphertext);
}

/// <summary>
/// AES-256-GCM, keyed by SHA-256(TokenEncryption:Key). The key is fully determined by the
/// env secret, so encrypted values stay decryptable across restarts/redeploys with no
/// key-ring volume to mount — deliberately sidestepping the ASP.NET Data Protection
/// key-persistence footgun for this single-instance MVP. Payload layout (base64):
/// [nonce(12) | tag(16) | ciphertext].
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public SecretProtector(IOptions<TokenEncryptionOptions> options)
    {
        var raw = options.Value.Key;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 16)
            throw new InvalidOperationException(
                "TokenEncryption:Key must be set to a strong secret (>=16 chars) to encrypt secrets at rest.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(raw)); // 32-byte AES-256 key
    }

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(output);
    }

    public string Unprotect(string ciphertext)
    {
        var input = Convert.FromBase64String(ciphertext);
        if (input.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext too short.");

        var nonce = input.AsSpan(0, NonceSize);
        var tag = input.AsSpan(NonceSize, TagSize);
        var cipher = input.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string? TryUnprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return null;
        try { return Unprotect(ciphertext); }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
    }
}
