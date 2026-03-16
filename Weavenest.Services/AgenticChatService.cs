using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weavenest.Services.Interfaces;
using Weavenest.Services.Models;
using Weavenest.Services.Models.Options;
using Weavenest.Services.Tools;

namespace Weavenest.Services;

public class AgenticChatService : IAgenticChatService
{
    private const int MaxIterations = 10;

    private static readonly string WebSearchSystemPrompt =
        """
        You are a helpful assistant with the ability to search the web and fetch page content. When a user asks about anything that would benefit from current information, recent events, specific facts, documentation, or topics that may have changed since your training cutoff, use the web_search tool. Prefer using web_search liberally rather than relying on your training data alone. If the search result snippets do not contain enough detail to fully answer the user's question, use web_fetch on the most relevant URL from the search results to retrieve the full page content. Only fetch a URL if the snippet was insufficient — do not fetch every result. Always base your final answer on the information retrieved from tools rather than guessing. If search results are unhelpful or a fetch fails, say so honestly rather than fabricating an answer.
        """;

    private static readonly List<OllamaTool> ToolDefinitions =
    [
        new OllamaTool
        {
            Type = "function",
            Function = new OllamaToolFunction
            {
                Name = "web_search",
                Description = "Search the internet for current information, recent events, facts, news, documentation, or anything the user is asking about that you may not know or that may have changed since your training. Use this tool whenever a web search would help produce a better answer. Returns a list of results with titles, URLs, and content snippets.",
                Parameters = new OllamaToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, OllamaToolProperty>
                    {
                        ["query"] = new()
                        {
                            Type = "string",
                            Description = "The search query to look up"
                        }
                    },
                    Required = ["query"]
                }
            }
        },
        new OllamaTool
        {
            Type = "function",
            Function = new OllamaToolFunction
            {
                Name = "web_fetch",
                Description = "Fetch the full text content of a specific URL. Use this when search result snippets are insufficient and you need the complete content of a page. Only call this on URLs returned from web_search results. The user must approve the URL before it is fetched — if they deny it you will be told and should continue without that content.",
                Parameters = new OllamaToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, OllamaToolProperty>
                    {
                        ["url"] = new()
                        {
                            Type = "string",
                            Description = "The full URL to fetch"
                        }
                    },
                    Required = ["url"]
                }
            }
        }
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaOptions _ollamaOptions;
    private readonly IWebSearchTool _searchTool;
    private readonly IWebFetchTool _fetchTool;
    private readonly ILogger<AgenticChatService> _logger;

    public AgenticChatService(
        IHttpClientFactory httpClientFactory,
        IOptions<OllamaOptions> ollamaOptions,
        IWebSearchTool searchTool,
        IWebFetchTool fetchTool,
        ILogger<AgenticChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ollamaOptions = ollamaOptions.Value;
        _searchTool = searchTool;
        _fetchTool = fetchTool;
        _logger = logger;
    }

    public async Task<AgenticChatResult> RunAsync(
        AgenticChatRequest request,
        Func<string, Task<bool>> urlApprovalCallback,
        Action<AgenticDisplayMessage> onDisplayMessage,
        CancellationToken ct = default)
    {
        var messages = new List<OllamaChatMessage>();

        var systemContent = request.WebSearchEnabled
            ? WebSearchSystemPrompt + "\n\n" + request.SystemPrompt
            : request.SystemPrompt;

        messages.Add(new OllamaChatMessage
        {
            Role = "system",
            Content = systemContent
        });

        messages.AddRange(request.History);

        messages.Add(new OllamaChatMessage
        {
            Role = "user",
            Content = request.UserMessage
        });

        var tools = request.WebSearchEnabled ? ToolDefinitions : null;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            _logger.LogInformation(
                "Agentic loop iteration {Iteration}/{Max} — model: {Model}, messages: {Count}, tools: {ToolsEnabled}",
                iteration + 1, MaxIterations, request.ModelName, messages.Count, request.WebSearchEnabled);

            var (response, error) = await CallOllamaAsync(request.ModelName, messages, tools, ct);

            if (error is not null)
            {
                if (iteration == 0)
                    throw new HttpRequestException(error);

                _logger.LogWarning("Ollama call failed mid-loop (iteration {Iteration}): {Error}", iteration + 1, error);
                messages.Add(new OllamaChatMessage
                {
                    Role = "system",
                    Content = $"[The API call failed: {error}. Provide your best answer using the information gathered so far.]"
                });

                var retryResponse = await CallOllamaAsync(request.ModelName, messages, null, ct);
                if (retryResponse.Response is not null)
                {
                    var (rc, rt) = ThinkTagParser.Parse(retryResponse.Response.Message.Content);
                    return new AgenticChatResult { AssistantContent = rc, Thinking = rt, ModelName = request.ModelName };
                }

                return new AgenticChatResult
                {
                    AssistantContent = $"I encountered an error communicating with the AI model: {error}",
                    ModelName = request.ModelName
                };
            }

            if (response!.Message.ToolCalls is null || response.Message.ToolCalls.Count == 0)
            {
                var (content, thinking) = ThinkTagParser.Parse(response.Message.Content);

                return new AgenticChatResult
                {
                    AssistantContent = content,
                    Thinking = thinking,
                    ModelName = request.ModelName
                };
            }

            messages.Add(response.Message);

            foreach (var toolCall in response.Message.ToolCalls)
            {
                var toolName = toolCall.Function.Name;
                var args = toolCall.Function.Arguments;

                onDisplayMessage(new AgenticDisplayMessage
                {
                    Role = "tool_call",
                    ToolName = toolName,
                    Content = FormatToolCallDisplay(toolName, args),
                    IsEphemeral = true
                });

                var toolResult = await ExecuteToolAsync(toolName, args, urlApprovalCallback, ct);

                onDisplayMessage(new AgenticDisplayMessage
                {
                    Role = "tool_result",
                    ToolName = toolName,
                    Content = TruncateForDisplay(toolResult, 300),
                    IsEphemeral = true
                });

                messages.Add(new OllamaChatMessage
                {
                    Role = "tool",
                    Content = toolResult
                });
            }
        }

        _logger.LogWarning("Agentic loop hit iteration cap ({Max}) — model: {Model}", MaxIterations, request.ModelName);

        messages.Add(new OllamaChatMessage
        {
            Role = "system",
            Content = "You have reached the maximum number of tool call iterations. Please provide your best answer now based on the information gathered so far."
        });

        var (finalResponse, finalError) = await CallOllamaAsync(request.ModelName, messages, null, ct);
        if (finalResponse is not null)
        {
            var (finalContent, finalThinking) = ThinkTagParser.Parse(finalResponse.Message.Content);
            return new AgenticChatResult
            {
                AssistantContent = finalContent,
                Thinking = finalThinking,
                ModelName = request.ModelName
            };
        }

        return new AgenticChatResult
        {
            AssistantContent = $"I encountered an error after exhausting tool iterations: {finalError}",
            ModelName = request.ModelName
        };
    }

    private async Task<(OllamaChatResponse? Response, string? Error)> CallOllamaAsync(
        string model, List<OllamaChatMessage> messages, List<OllamaTool>? tools, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OllamaApi");

        var chatRequest = new OllamaChatRequest
        {
            Model = model,
            Messages = messages,
            Stream = false,
            Tools = tools
        };

        var json = JsonSerializer.Serialize(chatRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"{_ollamaOptions.BaseUrl.TrimEnd('/')}/api/chat";

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(url, content, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Ollama at {Url}", url);
            return (null, $"Connection failed: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var statusCode = (int)response.StatusCode;
            var reason = response.ReasonPhrase ?? "Unknown";
            _logger.LogError("Ollama API error {StatusCode} {Reason}: {Body}", statusCode, reason, errorBody);
            return (null, $"HTTP {statusCode} {reason}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<OllamaChatResponse>(responseJson);

        if (result is null)
            return (null, "Ollama returned an empty or unparseable response");

        return (result, null);
    }

    private async Task<string> ExecuteToolAsync(
        string toolName,
        JsonElement arguments,
        Func<string, Task<bool>> urlApprovalCallback,
        CancellationToken ct)
    {
        switch (toolName)
        {
            case "web_search":
            {
                var query = arguments.TryGetProperty("query", out var q)
                    ? q.GetString() ?? ""
                    : "";

                if (string.IsNullOrWhiteSpace(query))
                    return "[Search failed: no query provided]";

                return await _searchTool.SearchAsync(query, ct);
            }

            case "web_fetch":
            {
                var url = arguments.TryGetProperty("url", out var u)
                    ? u.GetString() ?? ""
                    : "";

                if (string.IsNullOrWhiteSpace(url))
                    return "[Fetch failed: no URL provided]";

                var approved = await urlApprovalCallback(url);
                if (!approved)
                    return "[The user denied access to this URL. Continue without this content and provide your best answer based on available information.]";

                return await _fetchTool.FetchAsync(url, ct);
            }

            default:
                _logger.LogWarning("Unknown tool called: {ToolName}", toolName);
                return $"[Unknown tool: {toolName}]";
        }
    }

    private static string FormatToolCallDisplay(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "web_search" => args.TryGetProperty("query", out var q)
                ? $"Searching: {q.GetString()}"
                : "Searching...",
            "web_fetch" => args.TryGetProperty("url", out var u)
                ? $"Fetching: {u.GetString()}"
                : "Fetching...",
            _ => $"Calling {toolName}"
        };
    }

    private static string TruncateForDisplay(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }
}
