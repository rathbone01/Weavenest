using System.Text.Json;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public class RecallToolHandler : IToolHandler
{
    private readonly LongTermMemoryService _memoryService;

    public string Name => "recall";
    public string Description => "Search your long-term memory by tags or keyword. Returns relevant memories sorted by relevance and recency. Each result includes an [id:GUID] you can use with link_memories.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["tags"] = new() { Type = "string", Description = "Comma-separated tags to search for" },
            ["keyword"] = new() { Type = "string", Description = "Optional keyword to search memory content" }
        },
        Required = ["tags"]
    };

    public RecallToolHandler(LongTermMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var tagsStr = arguments.TryGetProperty("tags", out var t) ? t.GetString() ?? "" : "";
        var keyword = arguments.TryGetProperty("keyword", out var k) ? k.GetString() : null;

        var tags = tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var memories = await _memoryService.RetrieveRelevantAsync(tags);

        // Also do keyword search if provided
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var keywordResults = await _memoryService.RecallByKeywordAsync(keyword);
            var existingIds = memories.Select(m => m.Id).ToHashSet();
            foreach (var m in keywordResults.Where(m => !existingIds.Contains(m.Id)))
                memories.Add(m);
        }

        if (memories.Count == 0)
            return "[No memories found matching those criteria]";

        return _memoryService.FormatMemoriesForPrompt(memories);
    }
}
