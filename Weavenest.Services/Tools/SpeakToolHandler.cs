using System.Text.Json;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public class SpeakToolHandler : IToolHandler
{
    private readonly MindStateService _mindState;
    private readonly ShortTermMemoryService _shortTermMemory;

    public string Name => "speak";
    public string Description => "Send a message to the human in the chat window. Only use this when you genuinely want to communicate something. Silence is perfectly valid.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["message"] = new() { Type = "string", Description = "The message to speak to the human" }
        },
        Required = ["message"]
    };

    public SpeakToolHandler(MindStateService mindState, ShortTermMemoryService shortTermMemory)
    {
        _mindState = mindState;
        _shortTermMemory = shortTermMemory;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var message = arguments.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(message))
            return "[speak failed: empty message]";

        _mindState.RecordSpokenMessage(message);
        _shortTermMemory.AddEntry($"I said to the human: \"{message}\"", "spoke");

        return $"[Message delivered to the human: \"{message}\"]";
    }
}
