namespace Weavenest.DataAccess.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public ChatRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int? TokenCount { get; set; }
    public string? ModelName { get; set; }
}

public enum ChatRole
{
    System,
    User,
    Assistant
}
