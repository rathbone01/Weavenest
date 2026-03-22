using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Interfaces;

namespace Weavenest.Services;

public class DataMigrationService(
    IDbContextFactory<WeavenestDbContext> contextFactory,
    IEncryptionService encryption,
    ILogger<DataMigrationService> logger)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> MigrationLocks = new();

    public async Task MigrateUserDataIfNeededAsync(Guid userId)
    {
        if (!encryption.IsReady)
            return;

        var semaphore = MigrationLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();

        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();

            var user = await context.Users.FindAsync(userId);
            if (user is null || user.IsDataEncrypted)
                return;

            logger.LogInformation("Starting data encryption migration for user {UserId}", userId);

            // Encrypt user prompt
            if (user.UserPrompt is not null)
                user.UserPrompt = encryption.Encrypt(user.UserPrompt);

            // Encrypt all sessions and messages
            var sessions = await context.Sessions
                .Where(s => s.UserId == userId)
                .Include(s => s.Messages)
                .ToListAsync();

            var messageCount = 0;
            foreach (var session in sessions)
            {
                session.Title = encryption.Encrypt(session.Title);

                foreach (var message in session.Messages)
                {
                    message.Content = encryption.Encrypt(message.Content);
                    if (message.Thinking is not null)
                        message.Thinking = encryption.Encrypt(message.Thinking);
                    messageCount++;
                }
            }

            user.IsDataEncrypted = true;
            await context.SaveChangesAsync();

            logger.LogInformation("Data encryption migration complete for user {UserId}: {SessionCount} sessions, {MessageCount} messages encrypted",
                userId, sessions.Count, messageCount);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Re-encrypts all user data with a new key. Used during password changes.
    /// The old key must be set in CircuitSettings before calling, and the new key
    /// will be set after re-encryption completes.
    /// </summary>
    public async Task ReEncryptUserDataAsync(Guid userId, byte[] oldKey, byte[] newKey, string newPasswordHash, byte[] newEncryptionSalt)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var user = await context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        var sessions = await context.Sessions
            .Where(s => s.UserId == userId)
            .Include(s => s.Messages)
            .ToListAsync();

        // Decrypt with old key, re-encrypt with new key
        var oldEncryption = new EncryptionHelper(oldKey);
        var newEncryption = new EncryptionHelper(newKey);

        if (user.UserPrompt is not null)
            user.UserPrompt = newEncryption.Encrypt(oldEncryption.Decrypt(user.UserPrompt));

        foreach (var session in sessions)
        {
            session.Title = newEncryption.Encrypt(oldEncryption.Decrypt(session.Title));

            foreach (var message in session.Messages)
            {
                message.Content = newEncryption.Encrypt(oldEncryption.Decrypt(message.Content));
                if (message.Thinking is not null)
                    message.Thinking = newEncryption.Encrypt(oldEncryption.Decrypt(message.Thinking));
            }
        }

        user.PasswordHash = newPasswordHash;
        user.EncryptionSalt = newEncryptionSalt;
        await context.SaveChangesAsync();

        logger.LogInformation("Re-encryption complete for user {UserId}: {SessionCount} sessions re-encrypted", userId, sessions.Count);
    }
}
