using System.Security.Cryptography;
using System.Text;

namespace Weavenest.Services;

/// <summary>
/// Standalone AES-256-GCM encrypt/decrypt helper that operates with an explicit key.
/// Used by DataMigrationService for re-encryption during password changes,
/// where both old and new keys need to be used simultaneously.
/// </summary>
public class EncryptionHelper(byte[] key)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Encrypt(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        ciphertext.CopyTo(result, NonceSize + TagSize);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertextBase64)
    {
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
}
