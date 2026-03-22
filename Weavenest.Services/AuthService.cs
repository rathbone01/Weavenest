using Weavenest.DataAccess.Repositories;
using Weavenest.Services.Interfaces;

namespace Weavenest.Services;

public class AuthService(
    IUserRepository userRepository,
    TokenService tokenService,
    CircuitSettings circuitSettings,
    DataMigrationService dataMigration) : IAuthService
{
    public async Task<string?> LoginAsync(string username, string password)
    {
        var user = await userRepository.GetByUsernameAsync(username);
        if (user is null)
            return null;

        if (!HashHelper.VerifyPassword(password, user.PasswordHash))
            return null;

        // If the user has a legacy SHA-256 hash, upgrade to PBKDF2
        if (HashHelper.IsLegacyHash(user.PasswordHash))
        {
            var newHash = HashHelper.HashPassword(password);
            var encryptionSalt = HashHelper.GenerateEncryptionSalt();
            await userRepository.UpdatePasswordHashAsync(user.Id, newHash, encryptionSalt);
            user.PasswordHash = newHash;
            user.EncryptionSalt = encryptionSalt;
        }

        // Derive and store the encryption key for this circuit
        circuitSettings.EncryptionKey = HashHelper.DeriveEncryptionKey(password, user.EncryptionSalt);

        // Encrypt existing plaintext data on first login after upgrade
        await dataMigration.MigrateUserDataIfNeededAsync(user.Id);

        return tokenService.GenerateToken(user);
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(Guid userId, string oldPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            return (false, "New password is required.");

        var user = await userRepository.GetByIdAsync(userId);
        if (user is null)
            return (false, "User not found.");

        if (!HashHelper.VerifyPassword(oldPassword, user.PasswordHash))
            return (false, "Current password is incorrect.");

        var oldKey = circuitSettings.EncryptionKey
            ?? throw new InvalidOperationException("Encryption key not loaded.");

        var newEncryptionSalt = HashHelper.GenerateEncryptionSalt();
        var newKey = HashHelper.DeriveEncryptionKey(newPassword, newEncryptionSalt);
        var newPasswordHash = HashHelper.HashPassword(newPassword);

        await dataMigration.ReEncryptUserDataAsync(userId, oldKey, newKey, newPasswordHash, newEncryptionSalt);

        // Update the circuit's encryption key to the new one
        circuitSettings.EncryptionKey = newKey;

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RegisterAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            return (false, "Username is required.");

        if (string.IsNullOrWhiteSpace(password))
            return (false, "Password is required.");

        if (await userRepository.UsernameExistsAsync(username))
            return (false, "Username is already taken.");

        var hashedPassword = HashHelper.HashPassword(password);
        var encryptionSalt = HashHelper.GenerateEncryptionSalt();
        await userRepository.CreateUserAsync(username, hashedPassword, encryptionSalt);
        return (true, null);
    }
}
