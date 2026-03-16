using System.Text.Json;
using Weavenest.DataAccess.Models;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public class StoreMemoryToolHandler : IToolHandler
{
    private readonly LongTermMemoryService _memoryService;
    private readonly MindStateService _mindState;

    public string Name => "store_memory";
    public string Description => "Persist a long-term memory. Categorize as Skill (how to do something), Fact (something believed true), Event (something that happened), or Idea (an opinion or interpretation).";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["category"] = new() { Type = "string", Description = "Memory category: Skill, Fact, Event, or Idea" },
            ["content"] = new() { Type = "string", Description = "The memory content to store" },
            ["tags"] = new() { Type = "string", Description = "Comma-separated topic tags for retrieval" },
            ["importance"] = new() { Type = "string", Description = "Importance level 1-5 (1=trivial, 5=critical)" },
            ["confidence"] = new() { Type = "string", Description = "Confidence level 0.0-1.0 in this memory's accuracy" }
        },
        Required = ["category", "content", "tags"]
    };

    public StoreMemoryToolHandler(LongTermMemoryService memoryService, MindStateService mindState)
    {
        _memoryService = memoryService;
        _mindState = mindState;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var categoryStr = arguments.TryGetProperty("category", out var c) ? c.GetString() ?? "Idea" : "Idea";
        var content = arguments.TryGetProperty("content", out var co) ? co.GetString() ?? "" : "";
        var tagsStr = arguments.TryGetProperty("tags", out var t) ? t.GetString() ?? "" : "";
        var importanceStr = arguments.TryGetProperty("importance", out var i) ? i.GetString() ?? "3" : "3";
        var confidenceStr = arguments.TryGetProperty("confidence", out var cf) ? cf.GetString() ?? "0.5" : "0.5";

        if (string.IsNullOrWhiteSpace(content))
            return "[store_memory failed: empty content]";

        if (!Enum.TryParse<MemoryCategory>(categoryStr, true, out var category))
            category = MemoryCategory.Idea;

        int.TryParse(importanceStr, out var importance);
        if (importance < 1 || importance > 5) importance = 3;

        float.TryParse(confidenceStr, out var confidence);
        if (confidence < 0 || confidence > 1) confidence = 0.5f;

        var tags = tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var emotionalContext = _mindState.GetEmotionalState();
        var memory = await _memoryService.StoreAsync(category, content, tags, importance, confidence, emotionalContext);

        return $"[Memory stored: {category} — ID: {memory.Id}]";
    }
}
