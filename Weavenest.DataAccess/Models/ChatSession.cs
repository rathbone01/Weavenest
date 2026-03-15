namespace Weavenest.DataAccess.Models;

public class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New Chat";
    public string? ModelName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = [];
}
