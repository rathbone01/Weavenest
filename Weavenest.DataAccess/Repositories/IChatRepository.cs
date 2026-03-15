using Weavenest.DataAccess.Models;

namespace Weavenest.DataAccess.Repositories;

public interface IChatRepository
{
    Task<ChatSession> CreateSessionAsync(string? title = null, string? modelName = null);
    Task<IReadOnlyList<ChatSession>> GetSessionsAsync();
    Task<ChatSession?> GetSessionByIdAsync(Guid sessionId);
    Task<bool> DeleteSessionAsync(Guid sessionId);
    Task<ChatSession> UpdateSessionTitleAsync(Guid sessionId, string title);
    Task<ChatMessage> AddMessageAsync(Guid sessionId, ChatRole role, string content, int? tokenCount = null, string? modelName = null);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid sessionId);
}
