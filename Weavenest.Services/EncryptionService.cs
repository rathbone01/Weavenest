using System.Security.Cryptography;
using System.Text;
using Weavenest.DataAccess.Interfaces;

namespace Weavenest.Services;

public class EncryptionService(CircuitSettings circuitSettings) : IEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public bool IsReady => circuitSettings.EncryptionKey is not null;

    public string Encrypt(string plaintext)
    {
        var key = circuitSettings.EncryptionKey
            ?? throw new InvalidOperationException("Encryption key is not loaded. User must be authenticated.");

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Pack as: nonce + tag + ciphertext
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        ciphertext.CopyTo(result, NonceSize + TagSize);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertextBase64)
    {
        var key = circuitSettings.EncryptionKey
            ?? throw new InvalidOperationException("Encryption key is not loaded. User must be authenticated.");

        var packed = Convert.FromBase64String(ciphertextBase64);

        if (packed.Length < NonceSize + TagSize)
            throw new CryptographicException("Invalid ciphertext: data too short.");

        var nonce = packed.AsSpan(0, NonceSize);
        var tag = packed.AsSpan(NonceSize, TagSize);
        var ciphertext = packed.AsSpan(NonceSize + TagSize);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    public string? EncryptNullable(string? plaintext)
        => plaintext is null ? null : Encrypt(plaintext);

    public string? DecryptNullable(string? ciphertext)
        => ciphertext is null ? null : Decrypt(ciphertext);
}
