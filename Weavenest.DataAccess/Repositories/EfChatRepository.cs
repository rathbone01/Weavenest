using Microsoft.EntityFrameworkCore;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Models;
using Weavenest.DataAccess.Interfaces;

namespace Weavenest.DataAccess.Repositories;

public class EfChatRepository(
    IDbContextFactory<WeavenestDbContext> contextFactory,
    IEncryptionService encryption) : IChatRepository
{
    public async Task<ChatSession> CreateSessionAsync(Guid userId, string? title = null, string? modelName = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var plaintextTitle = title ?? "New Chat";
        var session = new ChatSession
        {
            Title = encryption.IsReady ? encryption.Encrypt(plaintextTitle) : plaintextTitle,
            ModelName = modelName,
            UserId = userId
        };

        context.Sessions.Add(session);
        await context.SaveChangesAsync();

        // Return with plaintext title for the caller
        session.Title = plaintextTitle;
        return session;
    }

    public async Task<IReadOnlyList<ChatSession>> GetSessionsAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var sessions = await context.Sessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();

        if (encryption.IsReady)
        {
            foreach (var session in sessions)
                DecryptSession(session);
        }

        return sessions;
    }

    public async Task<ChatSession?> GetSessionByIdAsync(Guid sessionId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var session = await context.Sessions
            .Include(s => s.Messages.OrderBy(m => m.Timestamp))
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session is not null && encryption.IsReady)
        {
            DecryptSession(session);
            foreach (var msg in session.Messages)
                DecryptMessage(msg);
        }

        return session;
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

        session.Title = encryption.IsReady ? encryption.Encrypt(title) : title;
        session.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Return with plaintext title
        session.Title = title;
        return session;
    }

    public async Task<ChatMessage> AddMessageAsync(Guid sessionId, ChatRole role, string content, int? tokenCount = null, string? modelName = null, string? thinking = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var session = await context.Sessions.FindAsync(sessionId)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found");

        var encryptedContent = encryption.IsReady ? encryption.Encrypt(content) : content;
        var encryptedThinking = string.IsNullOrWhiteSpace(thinking)
            ? null
            : encryption.IsReady ? encryption.Encrypt(thinking) : thinking;

        var message = new ChatMessage
        {
            SessionId = sessionId,
            Role = role,
            Content = encryptedContent,
            TokenCount = tokenCount,
            ModelName = modelName,
            Thinking = encryptedThinking
        };

        context.Messages.Add(message);
        session.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Return with plaintext for the caller
        message.Content = content;
        message.Thinking = string.IsNullOrWhiteSpace(thinking) ? null : thinking;
        return message;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid sessionId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var messages = await context.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        if (encryption.IsReady)
        {
            foreach (var msg in messages)
                DecryptMessage(msg);
        }

        return messages;
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

        if (encryption.IsReady)
            DecryptSession(session);

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

        if (encryption.IsReady)
            DecryptSession(session);

        return session;
    }

    public async Task<IReadOnlyList<ChatSession>> SearchSessionsAsync(Guid userId, string query)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var normalizedQuery = query.Trim();

        if (encryption.IsReady)
        {
            // With encryption, we must load all sessions and search client-side
            var sessions = await context.Sessions
                .Where(s => s.UserId == userId)
                .Include(s => s.Messages)
                .OrderByDescending(s => s.UpdatedAt)
                .ToListAsync();

            var results = new List<ChatSession>();
            foreach (var session in sessions)
            {
                DecryptSession(session);
                foreach (var msg in session.Messages)
                    DecryptMessage(msg);

                var titleMatch = session.Title.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
                var contentMatch = session.Messages.Any(m =>
                    m.Content.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));

                if (titleMatch || contentMatch)
                    results.Add(session);
            }

            return results;
        }

        // Unencrypted fallback (pre-migration users)
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

    private void DecryptSession(ChatSession session)
    {
        try { session.Title = encryption.Decrypt(session.Title); }
        catch { /* Title may be unencrypted (pre-migration) */ }
    }

    private void DecryptMessage(ChatMessage message)
    {
        try { message.Content = encryption.Decrypt(message.Content); }
        catch { /* Content may be unencrypted (pre-migration) */ }

        if (message.Thinking is not null)
        {
            try { message.Thinking = encryption.Decrypt(message.Thinking); }
            catch { /* Thinking may be unencrypted (pre-migration) */ }
        }
    }
}
