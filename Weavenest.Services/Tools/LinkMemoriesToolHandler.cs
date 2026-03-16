using System.Text.Json;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public class LinkMemoriesToolHandler : IToolHandler
{
    private readonly LongTermMemoryService _memoryService;

    public string Name => "link_memories";
    public string Description => "Create an associative link between two memories by their IDs. This helps form conceptual connections between related knowledge.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["id1"] = new() { Type = "string", Description = "First memory ID (GUID)" },
            ["id2"] = new() { Type = "string", Description = "Second memory ID (GUID)" }
        },
        Required = ["id1", "id2"]
    };

    public LinkMemoriesToolHandler(LongTermMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var id1Str = arguments.TryGetProperty("id1", out var i1) ? i1.GetString() ?? "" : "";
        var id2Str = arguments.TryGetProperty("id2", out var i2) ? i2.GetString() ?? "" : "";

        if (!Guid.TryParse(id1Str, out var id1) || !Guid.TryParse(id2Str, out var id2))
            return "[link_memories failed: invalid GUID format]";

        await _memoryService.LinkAsync(id1, id2);
        return $"[Memories linked: {id1} <-> {id2}]";
    }
}
