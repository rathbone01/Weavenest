using Microsoft.EntityFrameworkCore;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public class EfChatRepository(IDbContextFactory<WeavenestDbContext> contextFactory) : IChatRepository
{
    public async Task<ChatSession> CreateSessionAsync(Guid userId, string? title = null, string? modelName = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var session = new ChatSession
        {
            Title = title ?? "New Chat",
            ModelName = modelName,
            UserId = userId
        };

        context.Sessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    public async Task<IReadOnlyList<ChatSession>> GetSessionsAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.Sessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<ChatSession?> GetSessionByIdAsync(Guid sessionId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.Sessions
            .Include(s => s.Messages.OrderBy(m => m.Timestamp))
            .FirstOrDefaultAsync(s => s.Id == sessionId);
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var session = await context.Sessions.FindAsync(sessionId);
        if (session is null)
            return false;

        context.Sessions.Remove(session);
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<ChatSession> UpdateSessionTitleAsync(Guid sessionId, string title)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var session = await context.Sessions.FindAsync(sessionId)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found");

        session.Title = title;
        session.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return session;
    }

    public async Task<ChatMessage> AddMessageAsync(Guid sessionId, ChatRole role, string content, int? tokenCount = null, string? modelName = null, string? thinking = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var session = await context.Sessions.FindAsync(sessionId)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found");

        var message = new ChatMessage
        {
            SessionId = sessionId,
            Role = role,
            Content = content,
            TokenCount = tokenCount,
            ModelName = modelName,
            Thinking = string.IsNullOrWhiteSpace(thinking) ? null : thinking
        };

        context.Messages.Add(message);
        session.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return message;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid sessionId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }

    public async Task<ChatSession> AddSessionToFolderAsync(Guid sessionId, Guid folderId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var session = await context.Sessions.FindAsync(sessionId)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found");

        var folderExists = await context.Folders.AnyAsync(f => f.Id == folderId);
        if (!folderExists)
            throw new KeyNotFoundException($"Folder {folderId} not found");

        session.FolderId = folderId;
        session.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return session;
    }

    public async Task<ChatSession> RemoveSessionFromFolderAsync(Guid sessionId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var session = await context.Sessions.FindAsync(sessionId)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found");

        session.FolderId = null;
        session.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return session;
    }

    public async Task<IReadOnlyList<ChatSession>> SearchSessionsAsync(Guid userId, string query)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var normalizedQuery = query.Trim();

        return await context.Sessions
            .Where(s => s.UserId == userId &&
                (s.Title.Contains(normalizedQuery) ||
                 s.Messages.Any(m => m.Content.Contains(normalizedQuery))))
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<string>> GetWhitelistedDomainsAsync(Guid sessionId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.WhitelistedDomains
            .Where(w => w.SessionId == sessionId)
            .OrderBy(w => w.CreatedAt)
            .Select(w => w.Domain)
            .ToListAsync();
    }

    public async Task AddWhitelistedDomainAsync(Guid sessionId, string domain)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var exists = await context.WhitelistedDomains
            .AnyAsync(w => w.SessionId == sessionId && w.Domain == domain);

        if (exists) return;

        context.WhitelistedDomains.Add(new WhitelistedDomain
        {
            SessionId = sessionId,
            Domain = domain
        });
        await context.SaveChangesAsync();
    }

    public async Task RemoveWhitelistedDomainAsync(Guid sessionId, string domain)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var entry = await context.WhitelistedDomains
            .FirstOrDefaultAsync(w => w.SessionId == sessionId && w.Domain == domain);

        if (entry is null) return;

        context.WhitelistedDomains.Remove(entry);
        await context.SaveChangesAsync();
    }

    public async Task ClearWhitelistedDomainsAsync(Guid sessionId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var entries = await context.WhitelistedDomains
            .Where(w => w.SessionId == sessionId)
            .ToListAsync();

        if (entries.Count == 0) return;

        context.WhitelistedDomains.RemoveRange(entries);
        await context.SaveChangesAsync();
    }
}
