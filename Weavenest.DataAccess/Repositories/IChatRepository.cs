using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public interface IChatRepository
{
    Task<ChatSession> CreateSessionAsync(Guid userId, string? title = null, string? modelName = null);
    Task<IReadOnlyList<ChatSession>> GetSessionsAsync(Guid userId);
    Task<ChatSession?> GetSessionByIdAsync(Guid userId, Guid sessionId);
    Task<bool> DeleteSessionAsync(Guid userId, Guid sessionId);
    Task<ChatSession> UpdateSessionTitleAsync(Guid userId, Guid sessionId, string title);
    Task<ChatMessage> AddMessageAsync(Guid userId, Guid sessionId, ChatRole role, string content, int? tokenCount = null, string? modelName = null, string? thinking = null);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid userId, Guid sessionId);
    Task<ChatSession> AddSessionToFolderAsync(Guid userId, Guid sessionId, Guid folderId);
    Task<ChatSession> RemoveSessionFromFolderAsync(Guid userId, Guid sessionId);
    Task<IReadOnlyList<ChatSession>> SearchSessionsAsync(Guid userId, string query);

    // Whitelisted domains (per-session)
    Task<IReadOnlyList<string>> GetWhitelistedDomainsAsync(Guid userId, Guid sessionId);
    Task AddWhitelistedDomainAsync(Guid userId, Guid sessionId, string domain);
    Task RemoveWhitelistedDomainAsync(Guid userId, Guid sessionId, string domain);
    Task ClearWhitelistedDomainsAsync(Guid userId, Guid sessionId);
}
