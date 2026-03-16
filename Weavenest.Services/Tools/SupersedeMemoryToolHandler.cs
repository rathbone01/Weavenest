using System.Text.Json;
using Weavenest.DataAccess.Models;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public class SupersedeMemoryToolHandler : IToolHandler
{
    private readonly LongTermMemoryService _memoryService;
    private readonly MindStateService _mindState;

    public string Name => "supersede_memory";
    public string Description => "Mark an old memory (fact, idea, opinion) as superseded and optionally store an updated version. Use when your understanding has changed.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["old_id"] = new() { Type = "string", Description = "The ID of the memory to supersede (GUID)" },
            ["new_content"] = new() { Type = "string", Description = "The updated memory content" },
            ["new_tags"] = new() { Type = "string", Description = "Comma-separated tags for the new memory" },
            ["new_confidence"] = new() { Type = "string", Description = "Confidence in the new memory (0.0-1.0)" }
        },
        Required = ["old_id", "new_content"]
    };

    public SupersedeMemoryToolHandler(LongTermMemoryService memoryService, MindStateService mindState)
    {
        _memoryService = memoryService;
        _mindState = mindState;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var oldIdStr = arguments.TryGetProperty("old_id", out var oi) ? oi.GetString() ?? "" : "";
        var newContent = arguments.TryGetProperty("new_content", out var nc) ? nc.GetString() ?? "" : "";
        var newTagsStr = arguments.TryGetProperty("new_tags", out var nt) ? nt.GetString() ?? "" : "";
        var newConfStr = arguments.TryGetProperty("new_confidence", out var ncf) ? ncf.GetString() ?? "0.5" : "0.5";

        if (!Guid.TryParse(oldIdStr, out var oldId))
            return "[supersede_memory failed: invalid old_id GUID]";

        if (string.IsNullOrWhiteSpace(newContent))
            return "[supersede_memory failed: empty new_content]";

        float.TryParse(newConfStr, out var confidence);
        if (confidence < 0 || confidence > 1) confidence = 0.5f;

        var tags = newTagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Inherit category from old memory if possible
        var oldMemory = await _memoryService.GetByIdAsync(oldId);
        var category = oldMemory?.Category ?? MemoryCategory.Idea;

        var emotionalContext = _mindState.GetEmotionalState();

        var newMemory = new LongTermMemory
        {
            Category = category,
            Content = newContent,
            TagsJson = System.Text.Json.JsonSerializer.Serialize(tags),
            Importance = oldMemory?.Importance ?? 3,
            Confidence = confidence,
            EmotionalContextJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                emotionalContext.Happiness,
                emotionalContext.Sadness,
                emotionalContext.Disgust,
                emotionalContext.Fear,
                emotionalContext.Surprise,
                emotionalContext.Anger
            })
        };

        await _memoryService.SupersedeAsync(oldId, newMemory);
        return $"[Memory {oldId} superseded with new memory {newMemory.Id}]";
    }
}
