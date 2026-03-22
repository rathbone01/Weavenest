using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Models;
using Weavenest.DataAccess.Interfaces;

namespace Weavenest.DataAccess.Repositories;

public class EfUserRepository(
    IDbContextFactory<WeavenestDbContext> contextFactory,
    IEncryptionService encryption,
    ILogger<EfUserRepository> logger) : IUserRepository
{
    public async Task<User?> GetByUsernameAsync(string username)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Users.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetByIdAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(userId);

        if (user is not null && encryption.IsReady && user.UserPrompt is not null)
        {
            try { user.UserPrompt = encryption.Decrypt(user.UserPrompt); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to decrypt user prompt for user {UserId}, may be pre-migration plaintext", userId);
            }
        }

        return user;
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Users.AnyAsync(u => u.Username == username);
    }

    public async Task<User> CreateUserAsync(string username, string passwordHash, byte[] encryptionSalt)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var user = new User
        {
            Username = username,
            PasswordHash = passwordHash,
            EncryptionSalt = encryptionSalt
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public async Task UpdateUserPromptAsync(Guid userId, string? userPrompt)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var user = await context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        user.UserPrompt = encryption.IsReady
            ? encryption.EncryptNullable(userPrompt)
            : userPrompt;
        await context.SaveChangesAsync();
    }

    public async Task UpdatePasswordAsync(Guid userId, string newPasswordHash, byte[] newEncryptionSalt)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var user = await context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        user.PasswordHash = newPasswordHash;
        user.EncryptionSalt = newEncryptionSalt;
        await context.SaveChangesAsync();
    }

    public async Task UpdatePasswordHashAsync(Guid userId, string newPasswordHash, byte[] encryptionSalt)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var user = await context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        user.PasswordHash = newPasswordHash;
        user.EncryptionSalt = encryptionSalt;
        await context.SaveChangesAsync();
    }
}
