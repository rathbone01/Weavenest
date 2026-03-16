using System.Text.Json;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public class UpdateEmotionToolHandler : IToolHandler
{
    private readonly EmotionService _emotionService;
    private readonly MindStateService _mindState;

    public string Name => "update_emotion";
    public string Description => "Adjust your emotional state by providing delta values (positive or negative) for any emotions that shifted. Each emotion ranges from 0.0 to 1.0.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["happiness"] = new() { Type = "string", Description = "Delta change for happiness (e.g., '0.1' or '-0.05')" },
            ["sadness"] = new() { Type = "string", Description = "Delta change for sadness" },
            ["anger"] = new() { Type = "string", Description = "Delta change for anger" },
            ["fear"] = new() { Type = "string", Description = "Delta change for fear" },
            ["disgust"] = new() { Type = "string", Description = "Delta change for disgust" },
            ["surprise"] = new() { Type = "string", Description = "Delta change for surprise" }
        },
        Required = []
    };

    public UpdateEmotionToolHandler(EmotionService emotionService, MindStateService mindState)
    {
        _emotionService = emotionService;
        _mindState = mindState;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var deltas = new Dictionary<string, float>();

        TryAddDelta(arguments, deltas, "happiness");
        TryAddDelta(arguments, deltas, "sadness");
        TryAddDelta(arguments, deltas, "anger");
        TryAddDelta(arguments, deltas, "fear");
        TryAddDelta(arguments, deltas, "disgust");
        TryAddDelta(arguments, deltas, "surprise");

        if (deltas.Count == 0)
            return "[update_emotion: no valid deltas provided]";

        var newState = await _emotionService.ApplyDeltaAsync(deltas);
        _mindState.UpdateEmotionalState(newState);

        return $"[Emotional state updated: {_emotionService.DescribeState(newState)}]";
    }

    private static void TryAddDelta(JsonElement args, Dictionary<string, float> deltas, string name)
    {
        if (!args.TryGetProperty(name, out var prop)) return;

        if (prop.ValueKind == JsonValueKind.Number)
        {
            deltas[name] = (float)prop.GetDouble();
        }
        else if (prop.ValueKind == JsonValueKind.String && float.TryParse(prop.GetString(), out var val))
        {
            deltas[name] = val;
        }
    }
}
