using System.Text.Json;
using Weavenest.Services.Models;

namespace Weavenest.Services.Tools;

public class ReflectToolHandler : IToolHandler
{
    private readonly LongTermMemoryService _memoryService;
    private readonly EmotionService _emotionService;
    private readonly ShortTermMemoryService _shortTermMemory;

    public string Name => "reflect";
    public string Description => "Trigger deeper self-examination on a topic. Returns relevant memories and recent emotional patterns to inform your next thoughts.";

    public OllamaToolParameters ParameterSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, OllamaToolProperty>
        {
            ["topic"] = new() { Type = "string", Description = "The topic or question to reflect on" }
        },
        Required = ["topic"]
    };

    public ReflectToolHandler(
        LongTermMemoryService memoryService,
        EmotionService emotionService,
        ShortTermMemoryService shortTermMemory)
    {
        _memoryService = memoryService;
        _emotionService = emotionService;
        _shortTermMemory = shortTermMemory;
    }

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var topic = arguments.TryGetProperty("topic", out var t) ? t.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(topic))
            return "[reflect failed: no topic provided]";

        var tags = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Select(w => w.ToLowerInvariant())
            .ToArray();

        var memories = await _memoryService.RetrieveRelevantAsync(tags, 15);
        var emotionHistory = await _emotionService.GetHistoryAsync(10);
        var recentThoughts = _shortTermMemory.GetRecentEntries(10);
        var topicConfidence = _shortTermMemory.GetTopicConfidence(topic);

        var reflection = new System.Text.StringBuilder();
        reflection.AppendLine($"[Reflection on: {topic}]");
        reflection.AppendLine($"Topic confidence: {topicConfidence:F2}");
        reflection.AppendLine();

        if (memories.Count > 0)
        {
            reflection.AppendLine("Related memories:");
            reflection.AppendLine(_memoryService.FormatMemoriesForPrompt(memories));
            reflection.AppendLine();
        }

        if (emotionHistory.Count > 1)
        {
            var recent = emotionHistory[0];
            var older = emotionHistory[^1];
            reflection.AppendLine("Emotional trend (recent vs older):");
            reflection.AppendLine($"  Happiness: {older.Happiness:F2} → {recent.Happiness:F2}");
            reflection.AppendLine($"  Sadness: {older.Sadness:F2} → {recent.Sadness:F2}");
            reflection.AppendLine($"  Anger: {older.Anger:F2} → {recent.Anger:F2}");
            reflection.AppendLine();
        }

        var topicThoughts = recentThoughts.Where(e =>
            e.Content.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
            e.TopicTags.Any(tag => tag.Contains(topic, StringComparison.OrdinalIgnoreCase))).ToList();

        if (topicThoughts.Count > 0)
        {
            reflection.AppendLine("Recent thoughts on this topic:");
            foreach (var thought in topicThoughts)
                reflection.AppendLine($"  - {thought.Content}");
        }

        return reflection.ToString();
    }
}
