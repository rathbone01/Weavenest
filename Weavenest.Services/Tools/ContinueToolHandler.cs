using System.Text.Json;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

/// <summary>
/// Signals that Jeremy wants an additional thinking/tool iteration this tick.
/// Without calling this, the tick ends after the current round of tool calls.
/// </summary>
public class ContinueToolHandler : IToolHandler
{
    public string Name => "continue";
    public string Description => "Request an additional iteration this tick to keep thinking, chain tool calls, or reflect further. Use only when you genuinely need more processing time — not as a habit.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["reason"] = new OllamaToolProperty
            {
                Type = "string",
                Description = "Why you need another iteration (e.g. 'waiting for recall results before deciding whether to speak')"
            }
        },
        Required = ["reason"]
    };

    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var reason = arguments.TryGetProperty("reason", out var r) ? r.GetString() : null;
        return Task.FromResult($"Iteration granted. Reason: {reason ?? "(none given)"}");
    }
}
