using Microsoft.EntityFrameworkCore;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public class EfUserRepository(IDbContextFactory<WeavenestDbContext> contextFactory) : IUserRepository
{
    public async Task<User?> GetByUsernameAsync(string username)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Users.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetByIdAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Users.FindAsync(userId);
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Users.AnyAsync(u => u.Username == username);
    }

    public async Task<User> CreateUserAsync(string username, string passwordHash)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var user = new User
        {
            Username = username,
            PasswordHash = passwordHash
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

        user.UserPrompt = userPrompt;
        await context.SaveChangesAsync();
    }
}
