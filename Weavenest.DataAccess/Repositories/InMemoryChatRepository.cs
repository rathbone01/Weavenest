using System.Collections.Concurrent;
using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public class InMemoryChatRepository : IChatRepository
{
    private readonly ConcurrentDictionary<Guid, ChatSession> _sessions = new();

    public Task<ChatSession> CreateSessionAsync(Guid userId, string? title = null, string? modelName = null)
    {
        var session = new ChatSession
        {
            Title = title ?? "New Chat",
            ModelName = modelName,
            UserId = userId
        };
        _sessions[session.Id] = session;
        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<ChatSession>> GetSessionsAsync(Guid userId)
    {
        var sessions = _sessions.Values
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToList() as IReadOnlyList<ChatSession>;
        return Task.FromResult(sessions!);
    }

    public Task<ChatSession?> GetSessionByIdAsync(Guid userId, Guid sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        if (session is not null && session.UserId != userId)
            return Task.FromResult<ChatSession?>(null);
        return Task.FromResult(session);
    }

    public Task<bool> DeleteSessionAsync(Guid userId, Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session) && session.UserId == userId)
            return Task.FromResult(_sessions.TryRemove(sessionId, out _));
        return Task.FromResult(false);
    }

    public Task<ChatSession> UpdateSessionTitleAsync(Guid userId, Guid sessionId, string title)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.UserId != userId)
            throw new KeyNotFoundException($"Session {sessionId} not found");

        session.Title = title;
        session.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult(session);
    }

    public Task<ChatMessage> AddMessageAsync(Guid userId, Guid sessionId, ChatRole role, string content, int? tokenCount = null, string? modelName = null, string? thinking = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.UserId != userId)
            throw new KeyNotFoundException($"Session {sessionId} not found");

        var message = new ChatMessage
        {
            SessionId = sessionId,
            Role = role,
            Content = content,
            TokenCount = tokenCount,
            ModelName = modelName,
            Thinking = string.IsNullOrWhiteSpace(thinking) ? null : thinking
        };

        session.Messages.Add(message);
        session.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid userId, Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.UserId != userId)
            return Task.FromResult<IReadOnlyList<ChatMessage>>([]);

        return Task.FromResult<IReadOnlyList<ChatMessage>>(session.Messages.ToList());
    }

    public Task<ChatSession> AddSessionToFolderAsync(Guid userId, Guid sessionId, Guid folderId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.UserId != userId)
            throw new KeyNotFoundException($"Session {sessionId} not found");

        session.FolderId = folderId;
        session.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult(session);
    }

    public Task<ChatSession> RemoveSessionFromFolderAsync(Guid userId, Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.UserId != userId)
            throw new KeyNotFoundException($"Session {sessionId} not found");

        session.FolderId = null;
        session.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<ChatSession>> SearchSessionsAsync(Guid userId, string query)
    {
        var normalizedQuery = query.Trim();
        var results = _sessions.Values
            .Where(s => s.UserId == userId &&
                (s.Title.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                 s.Messages.Any(m => m.Content.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(s => s.UpdatedAt)
            .ToList() as IReadOnlyList<ChatSession>;
        return Task.FromResult(results!);
    }

    // In-memory whitelist storage keyed by session ID
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _whitelists = new();

    public Task<IReadOnlyList<string>> GetWhitelistedDomainsAsync(Guid userId, Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.UserId != userId)
            return Task.FromResult<IReadOnlyList<string>>([]);

        if (_whitelists.TryGetValue(sessionId, out var domains))
            return Task.FromResult<IReadOnlyList<string>>(domains.ToList());
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task AddWhitelistedDomainAsync(Guid userId, Guid sessionId, string domain)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.UserId != userId)
            return Task.CompletedTask;

        var set = _whitelists.GetOrAdd(sessionId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        set.Add(domain);
        return Task.CompletedTask;
    }

    public Task RemoveWhitelistedDomainAsync(Guid userId, Guid sessionId, string domain)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.UserId != userId)
            return Task.CompletedTask;

        if (_whitelists.TryGetValue(sessionId, out var set))
            set.Remove(domain);
        return Task.CompletedTask;
    }

    public Task ClearWhitelistedDomainsAsync(Guid userId, Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.UserId != userId)
            return Task.CompletedTask;

        _whitelists.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
