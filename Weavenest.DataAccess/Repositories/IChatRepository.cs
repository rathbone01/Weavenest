using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public interface IChatRepository
{
    Task<ChatSession> CreateSessionAsync(Guid userId, string? title = null, string? modelName = null);
    Task<IReadOnlyList<ChatSession>> GetSessionsAsync(Guid userId);
    Task<ChatSession?> GetSessionByIdAsync(Guid sessionId);
    Task<bool> DeleteSessionAsync(Guid sessionId);
    Task<ChatSession> UpdateSessionTitleAsync(Guid sessionId, string title);
    Task<ChatMessage> AddMessageAsync(Guid sessionId, ChatRole role, string content, int? tokenCount = null, string? modelName = null, string? thinking = null);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid sessionId);
    Task<ChatSession> AddSessionToFolderAsync(Guid sessionId, Guid folderId);
    Task<ChatSession> RemoveSessionFromFolderAsync(Guid sessionId);
    Task<IReadOnlyList<ChatSession>> SearchSessionsAsync(Guid userId, string query);
}
