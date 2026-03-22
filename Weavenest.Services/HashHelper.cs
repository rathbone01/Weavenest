using System.Security.Cryptography;
using System.Text;

namespace Weavenest.Services;

public static class HashHelper
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int KeySize = 32; // AES-256
    private const int Iterations = 600_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    /// <summary>
    /// Hashes a password using PBKDF2-HMAC-SHA512 with a random salt.
    /// Returns "base64(salt):base64(hash)".
    /// </summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, Algorithm, HashSize);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies a password against a stored hash.
    /// Supports both the new PBKDF2 format ("salt:hash") and the legacy unsalted SHA-256 format (64-char hex).
    /// </summary>
    public static bool VerifyPassword(string password, string storedHash)
    {
        if (storedHash.Contains(':'))
        {
            var parts = storedHash.Split(':');
            if (parts.Length != 2) return false;

            var salt = Convert.FromBase64String(parts[0]);
            var expectedHash = Convert.FromBase64String(parts[1]);

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Iterations, Algorithm, HashSize);

            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }

        // Legacy SHA-256 format (64-char hex, no salt)
        return storedHash == LegacyHash(password);
    }

    /// <summary>
    /// Returns true if the stored hash is in the legacy SHA-256 format.
    /// </summary>
    public static bool IsLegacyHash(string storedHash) => !storedHash.Contains(':');

    /// <summary>
    /// Derives a 256-bit AES encryption key from a password and salt using PBKDF2.
    /// </summary>
    public static byte[] DeriveEncryptionKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, Algorithm, KeySize);
    }

    /// <summary>
    /// Generates a random 16-byte salt for encryption key derivation.
    /// </summary>
    public static byte[] GenerateEncryptionSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    /// <summary>
    /// Validates password meets minimum strength requirements.
    /// Since the encryption key is derived from the password, weak passwords mean weak encryption.
    /// </summary>
    public static (bool IsValid, string? Error) ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return (false, "Password is required.");

        if (password.Length < 12)
            return (false, "Password must be at least 12 characters long.");

        if (!password.Any(char.IsUpper))
            return (false, "Password must contain at least one uppercase letter.");

        if (!password.Any(char.IsLower))
            return (false, "Password must contain at least one lowercase letter.");

        if (!password.Any(char.IsDigit))
            return (false, "Password must contain at least one digit.");

        return (true, null);
    }

    /// <summary>
    /// Legacy SHA-256 hash for backward compatibility during migration.
    /// </summary>
    private static string LegacyHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
