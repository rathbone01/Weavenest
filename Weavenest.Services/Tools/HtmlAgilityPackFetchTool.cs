using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Weavenest.Services.Tools;

public partial class HtmlAgilityPackFetchTool : IWebFetchTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HtmlAgilityPackFetchTool> _logger;

    private static readonly HashSet<string> NodesToRemove = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "nav", "footer", "header", "aside", "noscript"
    };

    public HtmlAgilityPackFetchTool(
        IHttpClientFactory httpClientFactory,
        ILogger<HtmlAgilityPackFetchTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> FetchAsync(string url, CancellationToken ct = default)
    {
        try
        {
            url = RewriteRedditUrl(url);

            _logger.LogInformation("Fetching URL: {Url}", url);

            var client = _httpClientFactory.CreateClient("WebFetch");
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var reason = response.ReasonPhrase ?? "Unknown";
                _logger.LogError("Fetch returned HTTP {StatusCode} {Reason} for URL: {Url}", statusCode, reason, url);
                return $"[Fetch failed: HTTP {statusCode} {reason}. The page could not be retrieved.]";
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                !contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
            {
                return $"[Cannot parse content: the URL returned non-HTML content ({contentType})]";
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            return ParseHtml(html);
        }
        catch (TaskCanceledException)
        {
            return "[Fetch failed: request timed out]";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch URL: {Url}", url);
            return $"[Fetch failed: {ex.Message}]";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching URL: {Url}", url);
            return $"[Fetch failed: {ex.Message}]";
        }
    }

    private static string ParseHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var body = doc.DocumentNode.SelectSingleNode("//body");
        if (body is null)
            return "[No readable content found on page]";

        var nodesToRemove = body.Descendants()
            .Where(n => NodesToRemove.Contains(n.Name))
            .ToList();

        foreach (var node in nodesToRemove)
            node.Remove();

        var rawText = body.InnerText;
        var cleaned = CollapseWhitespace(rawText);

        return TruncateToWordLimit(cleaned, 4000);
    }

    private static string CollapseWhitespace(string text)
    {
        text = System.Net.WebUtility.HtmlDecode(text);
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    private static string TruncateToWordLimit(string text, int maxWords)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
            return string.Join(' ', words);

        return string.Join(' ', words.Take(maxWords)) + "\n[Content truncated to 4000 words]";
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

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
