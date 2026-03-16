using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weavenest.Services.Models.Options;

namespace Weavenest.Services.Tools;

public class SearXNGSearchTool : IWebSearchTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SearXNGOptions _options;
    private readonly ILogger<SearXNGSearchTool> _logger;

    public SearXNGSearchTool(
        IHttpClientFactory httpClientFactory,
        IOptions<SearXNGOptions> options,
        ILogger<SearXNGSearchTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SearchAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SearXNG");
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"{_options.BaseUrl.TrimEnd('/')}/search?q={encodedQuery}&format=json";

            _logger.LogInformation("SearXNG search: {Query}", query);

            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var reason = response.ReasonPhrase ?? "Unknown";
                _logger.LogError("SearXNG returned HTTP {StatusCode} {Reason} for query: {Query}", statusCode, reason, query);
                return $"[Search failed: HTTP {statusCode} {reason}. The search engine may be unavailable.]";
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var resultsArray))
                return "[No search results found]";

            var sb = new StringBuilder();
            var count = 0;

            foreach (var result in resultsArray.EnumerateArray())
            {
                if (count >= 8) break;

                var title = result.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var resultUrl = result.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var content = result.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

                resultUrl = RewriteRedditUrl(resultUrl);

                sb.AppendLine($"[{count + 1}] {title}");
                sb.AppendLine($"    URL: {resultUrl}");
                if (!string.IsNullOrWhiteSpace(content))
                    sb.AppendLine($"    Snippet: {content}");
                sb.AppendLine();

                count++;
            }

            if (count == 0)
                return "[No search results found]";

            _logger.LogInformation("SearXNG returned {Count} results for: {Query}", count, query);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearXNG search failed for query: {Query}", query);
            return $"[Search failed: {ex.Message}]";
        }
    }

    private static string RewriteRedditUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Host.Equals("www.reddit.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("reddit.com", StringComparison.OrdinalIgnoreCase)))
        {
            var builder = new UriBuilder(uri) { Host = "old.reddit.com" };
            return builder.Uri.ToString();
        }
        return url;
    }
}
