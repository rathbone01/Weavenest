namespace Weavenest.DataAccess.Models;

public class TickLog
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string? SubconsciousContent { get; set; }
    public string? ConsciousContent { get; set; }
    public string? SpokeContent { get; set; }
    public string? EmotionalStateBeforeJson { get; set; }
    public string? EmotionalStateAfterJson { get; set; }
    public string? ToolCallsJson { get; set; }
}
