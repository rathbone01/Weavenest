using Microsoft.Extensions.Logging;
using Weavenest.DataAccess.Repositories;
using Weavenest.Services.Interfaces;

namespace Weavenest.Services;

public class AuthService(
    IUserRepository userRepository,
    TokenService tokenService,
    CircuitSettings circuitSettings,
    DataMigrationService dataMigration,
    LoginRateLimiter rateLimiter,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<(string? Token, string? Error)> LoginAsync(string username, string password)
    {
        if (rateLimiter.IsLockedOut(username))
        {
            logger.LogWarning("Login attempt for locked-out user {Username}", username);
            return (null, "Too many failed attempts. Please try again later.");
        }

        var user = await userRepository.GetByUsernameAsync(username);
        if (user is null)
        {
            logger.LogWarning("Login attempt for non-existent user {Username}", username);
            rateLimiter.RecordFailedAttempt(username);
            return (null, "Invalid username or password.");
        }

        if (!HashHelper.VerifyPassword(password, user.PasswordHash))
        {
            logger.LogWarning("Failed login attempt for user {Username}", username);
            rateLimiter.RecordFailedAttempt(username);
            return (null, "Invalid username or password.");
        }

        // If the user has a legacy SHA-256 hash, upgrade to PBKDF2
        if (HashHelper.IsLegacyHash(user.PasswordHash))
        {
            logger.LogInformation("Upgrading legacy password hash for user {Username}", username);
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

        rateLimiter.RecordSuccessfulLogin(username);
        logger.LogInformation("User {Username} logged in successfully", username);

        return (tokenService.GenerateToken(user), null);
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(Guid userId, string oldPassword, string newPassword)
    {
        var (isValid, validationError) = HashHelper.ValidatePasswordStrength(newPassword);
        if (!isValid)
            return (false, validationError!);

        var user = await userRepository.GetByIdAsync(userId);
        if (user is null)
            return (false, "User not found.");

        if (!HashHelper.VerifyPassword(oldPassword, user.PasswordHash))
        {
            logger.LogWarning("Failed password change attempt for user {UserId} — incorrect current password", userId);
            return (false, "Current password is incorrect.");
        }

        var oldKey = circuitSettings.EncryptionKey
            ?? throw new InvalidOperationException("Encryption key not loaded.");

        var newEncryptionSalt = HashHelper.GenerateEncryptionSalt();
        var newKey = HashHelper.DeriveEncryptionKey(newPassword, newEncryptionSalt);
        var newPasswordHash = HashHelper.HashPassword(newPassword);

        await dataMigration.ReEncryptUserDataAsync(userId, oldKey, newKey, newPasswordHash, newEncryptionSalt);

        // Update the circuit's encryption key to the new one
        circuitSettings.EncryptionKey = newKey;

        logger.LogInformation("Password changed for user {UserId}", userId);

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RegisterAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            return (false, "Username is required.");

        var (isValid, validationError) = HashHelper.ValidatePasswordStrength(password);
        if (!isValid)
            return (false, validationError!);

        if (await userRepository.UsernameExistsAsync(username))
            return (false, "Username is already taken.");

        var hashedPassword = HashHelper.HashPassword(password);
        var encryptionSalt = HashHelper.GenerateEncryptionSalt();
        await userRepository.CreateUserAsync(username, hashedPassword, encryptionSalt);

        logger.LogInformation("New user registered: {Username}", username);

        return (true, null);
    }
}
