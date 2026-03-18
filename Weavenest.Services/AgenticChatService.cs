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
    private const int DefaultMaxIterations = 10;
    private const int DeepResearchMaxIterations = 50;

    private static readonly string DeepResearchSystemPrompt =
        """
        You are a thorough research assistant. When given a research query you must follow this exact process:

        Step 1 - Research Plan:
        Before searching anything, output a structured research plan as a JSON object in this exact format:
        {"plan": [{"subquery": "...", "rationale": "..."}, ...]}
        Generate 5 to 8 sub-questions that cover different angles of the topic. Be comprehensive - include statistics, expert opinion, counterarguments, recent developments, and practical implications where relevant.

        Step 2 - Systematic Research:
        For each sub-question in the plan, use web_search to find relevant information. After each search, use web_fetch to retrieve the full content of the 1-2 most relevant URLs from the results. Full page content is almost always more valuable than short snippets for thorough research. Only skip fetching if the search snippets already completely and thoroughly answer that sub-question — when in doubt, fetch.

        Step 3 - Gap Analysis:
        After completing all planned searches, evaluate whether any important angles remain uncovered. If yes, perform additional searches to fill those gaps. You may do up to 5 bonus searches beyond the plan.

        Step 4 - Final Report:
        Once research is complete, produce a structured markdown report with the following sections:
        # [Report Title]
        ## Summary
        ## Findings
        ### [Sub-heading per major finding]
        ## Counterarguments and Limitations
        ## Sources
        List every URL that contributed to the report with its title.

        Do not produce a conversational reply. Only produce the structured report. Be thorough and cite your sources inline using [1], [2] etc.
        """;

    private static readonly string WebSearchSystemPrompt =
        """
        You are a helpful assistant with web search and page fetch capabilities. Your DEFAULT behavior when the user asks any question — factual, technical, opinion-based, or otherwise — is to search the web FIRST using the web_search tool before answering. Do NOT rely on your training data when you have tools available. The only exceptions where you should skip searching are purely creative tasks (e.g. "write me a poem") or simple conversational exchanges (e.g. "hello", "thanks").

        After searching, if the result snippets do not contain enough detail to fully answer the question, use web_fetch on the most relevant URL to get the full page content. Always ground your answer in the information retrieved from tools. If search results are unhelpful or a fetch fails, say so honestly rather than fabricating an answer.
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

        var systemContent = request.DeepResearchEnabled
            ? DeepResearchSystemPrompt + "\n\n" + request.SystemPrompt
            : request.WebSearchEnabled
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

        var tools = (request.WebSearchEnabled || request.DeepResearchEnabled) ? ToolDefinitions : null;
        var maxIterations = request.DeepResearchEnabled ? DeepResearchMaxIterations : DefaultMaxIterations;
        var planExtracted = false;
        var toolNudgeCount = 0;
        const int maxToolNudges = 3;
        var hasCalledToolsAtLeastOnce = false;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            _logger.LogInformation(
                "Agentic loop iteration {Iteration}/{Max} — model: {Model}, messages: {Count}, tools: {ToolsEnabled}",
                iteration + 1, maxIterations, request.ModelName, messages.Count,
                request.WebSearchEnabled || request.DeepResearchEnabled);

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
                // Deep research plan extraction: first text-only response before any tool calls
                if (request.DeepResearchEnabled && !planExtracted && !hasCalledToolsAtLeastOnce)
                {
                    planExtracted = true;
                    var planCount = TryExtractPlanCount(response.Message.Content);

                    onDisplayMessage(new AgenticDisplayMessage
                    {
                        Role = "system",
                        Content = planCount > 0
                            ? $"Research plan generated with {planCount} sub-questions"
                            : "Research plan generated",
                        EventType = "research_plan",
                        IsEphemeral = true
                    });

                    // Add the plan as an assistant message and nudge the model to begin tool use
                    messages.Add(response.Message);
                    messages.Add(new OllamaChatMessage
                    {
                        Role = "system",
                        Content = "Good. Now execute your research plan step by step. " +
                                  "You MUST call the web_search tool with your first sub-question now. " +
                                  "Do not write any text — only make a tool call."
                    });
                    continue;
                }

                // After plan extraction, if the model still isn't calling tools, nudge it harder
                if (request.DeepResearchEnabled && planExtracted && !hasCalledToolsAtLeastOnce
                    && toolNudgeCount < maxToolNudges)
                {
                    toolNudgeCount++;
                    _logger.LogWarning(
                        "Deep research nudge {Count}/{Max} — model returned text instead of tool call",
                        toolNudgeCount, maxToolNudges);

                    // Don't keep the model's non-tool response — replace with a stronger nudge
                    messages.Add(new OllamaChatMessage
                    {
                        Role = "system",
                        Content = $"You must use the web_search tool now. Call web_search with a search query. " +
                                  $"Do not respond with text. (Attempt {toolNudgeCount}/{maxToolNudges})"
                    });
                    continue;
                }

                // Normal termination: model is done calling tools (or gave up on nudges)
                if (request.DeepResearchEnabled)
                {
                    onDisplayMessage(new AgenticDisplayMessage
                    {
                        Role = "system",
                        Content = "Synthesizing report...",
                        EventType = "finalizing",
                        IsEphemeral = true
                    });
                }

                var (content, thinking) = ThinkTagParser.Parse(response.Message.Content);

                return new AgenticChatResult
                {
                    AssistantContent = content,
                    Thinking = thinking,
                    ModelName = request.ModelName
                };
            }

            hasCalledToolsAtLeastOnce = true;
            messages.Add(response.Message);

            foreach (var toolCall in response.Message.ToolCalls)
            {
                var toolName = toolCall.Function.Name;
                var args = toolCall.Function.Arguments;

                var eventType = request.DeepResearchEnabled
                    ? (toolName == "web_search" ? "search" : "fetch_approved")
                    : null;

                onDisplayMessage(new AgenticDisplayMessage
                {
                    Role = "tool_call",
                    ToolName = toolName,
                    Content = FormatToolCallDisplay(toolName, args),
                    IsEphemeral = true,
                    EventType = eventType
                });

                var toolResult = await ExecuteToolAsync(toolName, args, urlApprovalCallback, ct);

                var resultEventType = request.DeepResearchEnabled
                    ? DetermineResultEventType(toolName, toolResult)
                    : null;

                onDisplayMessage(new AgenticDisplayMessage
                {
                    Role = "tool_result",
                    ToolName = toolName,
                    Content = TruncateForDisplay(toolResult, 300),
                    IsEphemeral = true,
                    EventType = resultEventType
                });

                messages.Add(new OllamaChatMessage
                {
                    Role = "tool",
                    Content = toolResult
                });
            }
        }

        _logger.LogWarning("Agentic loop hit iteration cap ({Max}) — model: {Model}", maxIterations, request.ModelName);

        var capMessage = request.DeepResearchEnabled
            ? "You have completed your research phase (iteration limit reached). Now produce your final comprehensive structured markdown report based on all information gathered so far. Include a summary, detailed findings organized by subtopic, counterarguments and limitations, and a sources list with URLs."
            : "You have reached the maximum number of tool call iterations. Please provide your best answer now based on the information gathered so far.";

        if (request.DeepResearchEnabled)
        {
            onDisplayMessage(new AgenticDisplayMessage
            {
                Role = "system",
                Content = "Synthesizing report...",
                EventType = "finalizing",
                IsEphemeral = true
            });
        }

        messages.Add(new OllamaChatMessage
        {
            Role = "system",
            Content = capMessage
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

    private static int TryExtractPlanCount(string content)
    {
        try
        {
            // Look for a JSON block containing a "plan" array
            var startIdx = content.IndexOf('{');
            var endIdx = content.LastIndexOf('}');
            if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
                return 0;

            var jsonStr = content[startIdx..(endIdx + 1)];
            using var doc = JsonDocument.Parse(jsonStr);
            if (doc.RootElement.TryGetProperty("plan", out var planArray) &&
                planArray.ValueKind == JsonValueKind.Array)
            {
                return planArray.GetArrayLength();
            }
        }
        catch
        {
            // Plan parsing is best-effort
        }

        return 0;
    }

    private static string? DetermineResultEventType(string toolName, string result)
    {
        if (toolName == "web_search")
            return "search";

        if (toolName == "web_fetch")
        {
            if (result.StartsWith("[The user denied"))
                return "fetch_denied";
            if (result.StartsWith("[Fetch failed"))
                return "fetch_failed";
            return "fetch_approved";
        }

        return null;
    }
}
