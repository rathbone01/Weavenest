using System.Text.Json;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public class WebSearchToolHandler : IToolHandler
{
    private readonly IWebSearchTool _searchTool;

    public string Name => "web_search";
    public string Description => "Search the internet for current information, recent events, facts, or documentation. Returns results with titles, URLs, and content snippets.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["query"] = new() { Type = "string", Description = "The search query to look up" }
        },
        Required = ["query"]
    };

    public WebSearchToolHandler(IWebSearchTool searchTool)
    {
        _searchTool = searchTool;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(query))
            return "[web_search failed: no query provided]";

        return await _searchTool.SearchAsync(query, ct);
    }
}
