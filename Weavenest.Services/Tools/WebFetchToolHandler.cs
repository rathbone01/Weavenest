using System.Text.Json;
using Microsoft.Extensions.Options;
using Weavenest.Services.Models;
using Weavenest.Services.Models.Options;

namespace Weavenest.Services.Tools;

public class WebFetchToolHandler : IToolHandler
{
    private readonly IWebFetchTool _fetchTool;
    private readonly MindSettings _settings;

    public string Name => "web_fetch";
    public string Description => "Fetch the full text content of a specific URL. Use when search result snippets are insufficient and you need the complete content. Only whitelisted domains are allowed.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["url"] = new() { Type = "string", Description = "The full URL to fetch" }
        },
        Required = ["url"]
    };

    public WebFetchToolHandler(IWebFetchTool fetchTool, IOptions<MindSettings> settings)
    {
        _fetchTool = fetchTool;
        _settings = settings.Value;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var url = arguments.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(url))
            return "[web_fetch failed: no URL provided]";

        if (!IsWhitelisted(url))
        {
            var allowed = string.Join(", ", _settings.WhitelistedDomains);
            return $"[web_fetch blocked: domain not in whitelist. Allowed domains: {allowed}]";
        }

        return await _fetchTool.FetchAsync(url, ct);
    }

    private bool IsWhitelisted(string url)
    {
        if (_settings.WhitelistedDomains.Count == 0)
            return true;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        return _settings.WhitelistedDomains.Any(domain =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }
}
