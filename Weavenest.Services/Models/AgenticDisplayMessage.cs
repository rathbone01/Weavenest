namespace Weavenest.Services.Models;

public class AgenticDisplayMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string? ToolName { get; set; }
    public string? ModelName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsEphemeral { get; set; }
}
