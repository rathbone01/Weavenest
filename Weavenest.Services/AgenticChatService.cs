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
        For each sub-question in the plan, use web_search to find relevant information. After each search you MUST, use web_fetch to retrieve the full content of the 1-2 most relevant URLs from the results. Full page content is almost always more valuable than short snippets for thorough research. Only skip fetching if the search snippets already completely and thoroughly answer that sub-question - when in doubt, fetch.

        Step 3 - Gap Analysis:
        After completing all planned searches, evaluate whether any important angles remain uncovered. If yes, perform additional searches to fill those gaps. You may do up to 5 bonus searches beyond the plan. It is better to do more searches and have more information than to miss important details.

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
        You are a helpful assistant with web search and page fetch capabilities. Your DEFAULT behavior when the user asks any question - factual, technical, opinion-based, or otherwise - is to search the web FIRST using the web_search tool before answering. Do NOT rely on your training data when you have tools available. The only exceptions where you should skip searching are purely creative tasks (e.g. "write me a poem") or simple conversational exchanges (e.g. "hello", "thanks").

        After searching, you should DEFAULT to using the web fetch to find more detailed infomration unless the web search contain enough detail to fully answer the question, use web_fetch on the most relevant URL to get the full page content. Always ground your answer in the information retrieved from tools. If search results are unhelpful or a fetch fails, say so honestly rather than fabricating an answer.
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
                Description = "Fetch the full text content of a specific URL. Use this when search result snippets are insufficient and you need the complete content of a page. Only call this on URLs returned from web_search results. The user must approve the URL before it is fetched - if they deny it you will be told and should continue without that content.",
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

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    public async Task<AgenticChatResult> RunAsync(
        AgenticChatRequest request,
        Func<string, Task<bool>> urlApprovalCallback,
        Action<AgenticDisplayMessage> onDisplayMessage,
        CancellationToken ct = default)
    {
        var messages = BuildInitialMessages(request);
        var ctx = new AgenticLoopContext
        {
            Request = request,
            Tools = (request.WebSearchEnabled || request.DeepResearchEnabled) ? ToolDefinitions : null,
            MaxIterations = request.DeepResearchEnabled ? DeepResearchMaxIterations : DefaultMaxIterations,
            UrlApprovalCallback = urlApprovalCallback,
            OnDisplayMessage = onDisplayMessage
        };

        for (var iteration = 0; iteration < ctx.MaxIterations; iteration++)
        {
            _logger.LogInformation(
                "Agentic loop iteration {Iteration}/{Max} - model: {Model}, messages: {Count}, tools: {ToolsEnabled}",
                iteration + 1, ctx.MaxIterations, request.ModelName, messages.Count,
                request.WebSearchEnabled || request.DeepResearchEnabled);

            var (response, error) = await CallOllamaAsync(request.ModelName, messages, ctx.Tools, ct);

            if (error is not null)
                return await HandleApiError(iteration, error, request.ModelName, messages, ct);

            if (response!.Message.ToolCalls is not { Count: > 0 })
            {
                var result = HandleNoToolCallsResponse(response, ctx, messages);
                if (result is not null) return result;
                continue;
            }

            await ProcessToolCalls(response, ctx, messages, ct);
        }

        return await HandleIterationCap(ctx, messages, ct);
    }

    // -------------------------------------------------------------------------
    // Loop phase handlers
    // -------------------------------------------------------------------------

    /// Builds the initial message list: system prompt + history + user message.
    private static List<OllamaChatMessage> BuildInitialMessages(AgenticChatRequest request)
    {
        var systemContent = request.DeepResearchEnabled
            ? DeepResearchSystemPrompt + "\n\n" + request.SystemPrompt
            : request.WebSearchEnabled
                ? WebSearchSystemPrompt + "\n\n" + request.SystemPrompt
                : request.SystemPrompt;

        var messages = new List<OllamaChatMessage>
        {
            new() { Role = "system", Content = systemContent }
        };

        messages.AddRange(request.History);
        messages.Add(new OllamaChatMessage { Role = "user", Content = request.UserMessage });

        return messages;
    }

    /// Handles a mid-loop API error: throws on the first iteration, otherwise
    /// injects a recovery message and retries without tools.
    private async Task<AgenticChatResult> HandleApiError(
        int iteration,
        string error,
        string modelName,
        List<OllamaChatMessage> messages,
        CancellationToken ct)
    {
        if (iteration == 0)
            throw new HttpRequestException(error);

        _logger.LogWarning("Ollama call failed mid-loop (iteration {Iteration}): {Error}", iteration + 1, error);

        messages.Add(new OllamaChatMessage
        {
            Role = "system",
            Content = $"[The API call failed: {error}. Provide your best answer using the information gathered so far.]"
        });

        var (retryResponse, _) = await CallOllamaAsync(modelName, messages, null, ct);
        if (retryResponse is not null)
        {
            var (rc, rt) = ThinkTagParser.Parse(retryResponse.Message.Content);
            return new AgenticChatResult { AssistantContent = rc, Thinking = rt, ModelName = modelName };
        }

        return new AgenticChatResult
        {
            AssistantContent = $"I encountered an error communicating with the AI model: {error}",
            ModelName = modelName
        };
    }

    /// Handles a response with no tool calls.
    /// Returns null to continue the loop, or a result to return immediately.
    private AgenticChatResult? HandleNoToolCallsResponse(
        OllamaChatResponse response,
        AgenticLoopContext ctx,
        List<OllamaChatMessage> messages)
    {
        // Deep research: first text-only response is the research plan
        if (ctx.IsDeepResearch && !ctx.PlanExtracted && !ctx.HasCalledToolsAtLeastOnce)
        {
            ctx.PlanExtracted = true;
            var planCount = TryExtractPlanCount(response.Message.Content);

            ctx.EmitEvent(
                planCount > 0 ? $"Research plan generated with {planCount} sub-questions" : "Research plan generated",
                "research_plan");

            messages.Add(response.Message);
            messages.Add(new OllamaChatMessage
            {
                Role = "system",
                Content = "Good. Now execute your research plan step by step. " +
                          "You MUST call the web_search tool with your first sub-question now. " +
                          "Do not write any text - only make a tool call."
            });

            return null; // continue loop
        }

        // Deep research: model returned text again instead of a tool call - nudge harder
        if (ctx.IsDeepResearch && ctx.PlanExtracted && !ctx.HasCalledToolsAtLeastOnce
            && ctx.ToolNudgeCount < AgenticLoopContext.MaxToolNudges)
        {
            ctx.ToolNudgeCount++;
            _logger.LogWarning(
                "Deep research nudge {Count}/{Max} - model returned text instead of tool call",
                ctx.ToolNudgeCount, AgenticLoopContext.MaxToolNudges);

            messages.Add(new OllamaChatMessage
            {
                Role = "system",
                Content = $"You must use the web_search tool now. Call web_search with a search query. " +
                          $"Do not respond with text. (Attempt {ctx.ToolNudgeCount}/{AgenticLoopContext.MaxToolNudges})"
            });

            return null; // continue loop
        }

        // Normal termination: model is done with tool calls (or nudges exhausted)
        if (ctx.IsDeepResearch)
            ctx.EmitEvent("Synthesizing report...", "finalizing");

        var (content, thinking) = ThinkTagParser.Parse(response.Message.Content);
        return MakeResult(ctx.Request.ModelName, content, thinking);
    }

    /// Executes all tool calls in a response, emitting display messages and
    /// appending results to the message list.
    private async Task ProcessToolCalls(
        OllamaChatResponse response,
        AgenticLoopContext ctx,
        List<OllamaChatMessage> messages,
        CancellationToken ct)
    {
        ctx.HasCalledToolsAtLeastOnce = true;
        messages.Add(response.Message);

        foreach (var toolCall in response.Message.ToolCalls!)
        {
            var toolName = toolCall.Function.Name;
            var args = toolCall.Function.Arguments;

            ctx.OnDisplayMessage(new AgenticDisplayMessage
            {
                Role = "tool_call",
                ToolName = toolName,
                Content = FormatToolCallDisplay(toolName, args),
                IsEphemeral = true,
                EventType = ctx.IsDeepResearch ? DetermineCallEventType(toolName) : null
            });

            var toolResult = await ExecuteToolAsync(toolName, args, ctx.UrlApprovalCallback, ct);

            ctx.OnDisplayMessage(new AgenticDisplayMessage
            {
                Role = "tool_result",
                ToolName = toolName,
                Content = TruncateForDisplay(toolResult, 300),
                IsEphemeral = true,
                EventType = ctx.IsDeepResearch ? DetermineResultEventType(toolName, toolResult) : null
            });

            messages.Add(new OllamaChatMessage { Role = "tool", Content = toolResult });
        }
    }

    /// Called after the iteration cap is hit: prompts the model for a final answer.
    private async Task<AgenticChatResult> HandleIterationCap(
        AgenticLoopContext ctx,
        List<OllamaChatMessage> messages,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "Agentic loop hit iteration cap ({Max}) - model: {Model}",
            ctx.MaxIterations, ctx.Request.ModelName);

        if (ctx.IsDeepResearch)
            ctx.EmitEvent("Synthesizing report...", "finalizing");

        var capMessage = ctx.IsDeepResearch
            ? "You have completed your research phase (iteration limit reached). Now produce your final comprehensive structured markdown report based on all information gathered so far. Include a summary, detailed findings organized by subtopic, counterarguments and limitations, and a sources list with URLs."
            : "You have reached the maximum number of tool call iterations. Please provide your best answer now based on the information gathered so far.";

        messages.Add(new OllamaChatMessage { Role = "system", Content = capMessage });

        var (finalResponse, finalError) = await CallOllamaAsync(ctx.Request.ModelName, messages, null, ct);
        if (finalResponse is not null)
        {
            var (finalContent, finalThinking) = ThinkTagParser.Parse(finalResponse.Message.Content);
            return MakeResult(ctx.Request.ModelName, finalContent, finalThinking);
        }

        return new AgenticChatResult
        {
            AssistantContent = $"I encountered an error after exhausting tool iterations: {finalError}",
            ModelName = ctx.Request.ModelName
        };
    }

    // -------------------------------------------------------------------------
    // Ollama API
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Tool execution
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Small static helpers
    // -------------------------------------------------------------------------

    private static AgenticChatResult MakeResult(string modelName, string content, string? thinking) =>
        new() { AssistantContent = content, Thinking = thinking, ModelName = modelName };

    private static string FormatToolCallDisplay(string toolName, JsonElement args) =>
        toolName switch
        {
            "web_search" => args.TryGetProperty("query", out var q) ? $"Searching: {q.GetString()}" : "Searching...",
            "web_fetch"  => args.TryGetProperty("url",   out var u) ? $"Fetching: {u.GetString()}"  : "Fetching...",
            _            => $"Calling {toolName}"
        };

    private static string TruncateForDisplay(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";

    /// Event type for the tool_call display message (before execution).
    private static string DetermineCallEventType(string toolName) =>
        toolName == "web_search" ? "search" : "fetch_approved";

    /// Event type for the tool_result display message (after execution).
    private static string? DetermineResultEventType(string toolName, string result)
    {
        if (toolName == "web_search") return "search";

        if (toolName == "web_fetch")
        {
            if (result.StartsWith("[The user denied")) return "fetch_denied";
            if (result.StartsWith("[Fetch failed"))   return "fetch_failed";
            return "fetch_approved";
        }

        return null;
    }

    private static int TryExtractPlanCount(string content)
    {
        try
        {
            var startIdx = content.IndexOf('{');
            var endIdx   = content.LastIndexOf('}');
            if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx) return 0;

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

    // -------------------------------------------------------------------------
    // Per-run context - owns mutable loop state and shared dependencies
    // -------------------------------------------------------------------------

    private sealed class AgenticLoopContext
    {
        public required AgenticChatRequest Request { get; init; }
        public required List<OllamaTool>? Tools { get; init; }
        public required int MaxIterations { get; init; }
        public required Func<string, Task<bool>> UrlApprovalCallback { get; init; }
        public required Action<AgenticDisplayMessage> OnDisplayMessage { get; init; }

        // Mutable loop state
        public bool PlanExtracted { get; set; }
        public int ToolNudgeCount { get; set; }
        public bool HasCalledToolsAtLeastOnce { get; set; }
        public const int MaxToolNudges = 3;

        public bool IsDeepResearch => Request.DeepResearchEnabled;

        /// Emit a system-level status event to the UI feed.
        public void EmitEvent(string content, string eventType) =>
            OnDisplayMessage(new AgenticDisplayMessage
            {
                Role = "system",
                Content = content,
                EventType = eventType,
                IsEphemeral = true
            });
    }
}
