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

    public Task<ChatSession?> GetSessionByIdAsync(Guid sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task<bool> DeleteSessionAsync(Guid sessionId)
    {
        var removed = _sessions.TryRemove(sessionId, out _);
        return Task.FromResult(removed);
    }

    public Task<ChatSession> UpdateSessionTitleAsync(Guid sessionId, string title)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException($"Session {sessionId} not found");

        session.Title = title;
        session.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult(session);
    }

    public Task<ChatMessage> AddMessageAsync(Guid sessionId, ChatRole role, string content, int? tokenCount = null, string? modelName = null, string? thinking = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
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

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return Task.FromResult<IReadOnlyList<ChatMessage>>([]);

        return Task.FromResult<IReadOnlyList<ChatMessage>>(session.Messages.ToList());
    }

    public Task<ChatSession> AddSessionToFolderAsync(Guid sessionId, Guid folderId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException($"Session {sessionId} not found");

        session.FolderId = folderId;
        session.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult(session);
    }

    public Task<ChatSession> RemoveSessionFromFolderAsync(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException($"Session {sessionId} not found");

        session.FolderId = null;
        session.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult(session);
    }
}
