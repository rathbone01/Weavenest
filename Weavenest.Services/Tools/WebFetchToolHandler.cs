using System.Text.Json;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public class WebFetchToolHandler : IToolHandler
{
    private readonly IWebFetchTool _fetchTool;

    public string Name => "web_fetch";
    public string Description => "Fetch the full text content of a specific URL. Use when search result snippets are insufficient and you need the complete content.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["url"] = new() { Type = "string", Description = "The full URL to fetch" }
        },
        Required = ["url"]
    };

    public WebFetchToolHandler(IWebFetchTool fetchTool)
    {
        _fetchTool = fetchTool;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var url = arguments.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(url))
            return "[web_fetch failed: no URL provided]";

        return await _fetchTool.FetchAsync(url, ct);
    }
}
