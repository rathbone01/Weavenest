using System.Text.Json;
using Microsoft.Extensions.Logging;
using Weavenest.Services.Models;
using Weavenest.Services.Tools;

namespace Weavenest.Services;

public class ToolDispatchService
{
    private readonly Dictionary<string, IToolHandler> _handlers;
    private readonly MindStateService _mindState;
    private readonly ILogger<ToolDispatchService> _logger;

    public ToolDispatchService(
        IEnumerable<IToolHandler> handlers,
        MindStateService mindState,
        ILogger<ToolDispatchService> logger)
    {
        _handlers = handlers.ToDictionary(h => h.Name, h => h, StringComparer.OrdinalIgnoreCase);
        _mindState = mindState;
        _logger = logger;
    }

    public List<OllamaTool> GetToolDefinitions()
    {
        return _handlers.Values.Select(h => new OllamaTool
        {
            Type = "function",
            Function = new OllamaToolFunction
            {
                Name = h.Name,
                Description = h.Description,
                Parameters = h.ParameterSchema
            }
        }).ToList();
    }

    public async Task<string> DispatchAsync(string toolName, JsonElement arguments, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
        {
            _logger.LogWarning("Unknown tool called: {ToolName}", toolName);
            return $"[Error: Unknown tool '{toolName}'. Available tools: {string.Join(", ", _handlers.Keys)}]";
        }

        // Ollama sometimes returns arguments as a JSON-encoded string rather than a JSON object.
        // Normalize to an object element so all handlers can use TryGetProperty safely.
        if (arguments.ValueKind == JsonValueKind.String)
        {
            var raw = arguments.GetString() ?? "{}";
            _logger.LogDebug("Tool {ToolName} arguments were a JSON-encoded string — parsing inner JSON", toolName);
            arguments = JsonDocument.Parse(raw).RootElement;
        }

        var argsStr = arguments.ToString();
        string result;
        bool succeeded;

        try
        {
            _logger.LogInformation("Dispatching tool: {ToolName} with args: {Args}", toolName, argsStr);
            result = await handler.ExecuteAsync(arguments, ct);
            succeeded = !result.StartsWith("[Error");
            _logger.LogInformation("Tool {ToolName} result: {Result}", toolName, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} failed", toolName);
            result = $"[Error executing {toolName}: {ex.Message}]";
            succeeded = false;
        }

        _mindState.RecordToolCall(new ToolCallRecord(toolName, argsStr, result, succeeded, DateTime.UtcNow));
        return result;
    }

    public async Task<List<(string ToolName, string Result)>> DispatchAllAsync(
        List<OllamaToolCall> toolCalls, CancellationToken ct)
    {
        var results = new List<(string, string)>();
        foreach (var call in toolCalls)
        {
            var result = await DispatchAsync(call.Function.Name, call.Function.Arguments, ct);
            results.Add((call.Function.Name, result));
        }
        return results;
    }
}
