using Weavenest.Services.Models;

namespace Weavenest.Services.Interfaces;

public class AgenticChatRequest
{
    public required string ModelName { get; set; }
    public required string SystemPrompt { get; set; }
    public required List<OllamaChatMessage> History { get; set; }
    public required string UserMessage { get; set; }
    public bool WebSearchEnabled { get; set; } = true;
}

public class AgenticChatResult
{
    public string AssistantContent { get; set; } = "";
    public string? Thinking { get; set; }
    public string? ModelName { get; set; }
}

public interface IAgenticChatService
{
    Task<AgenticChatResult> RunAsync(
        AgenticChatRequest request,
        Func<string, Task<bool>> urlApprovalCallback,
        Action<AgenticDisplayMessage> onDisplayMessage,
        CancellationToken ct = default);
}
