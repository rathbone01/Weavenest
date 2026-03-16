using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weavenest.Services.Models.Options;

namespace Weavenest.Services;

public class ShortTermMemoryService
{
    private readonly MindStateService _mindState;
    private readonly MindSettings _settings;
    private readonly ILogger<ShortTermMemoryService> _logger;

    public ShortTermMemoryService(
        MindStateService mindState,
        IOptions<MindSettings> settings,
        ILogger<ShortTermMemoryService> logger)
    {
        _mindState = mindState;
        _settings = settings.Value;
        _logger = logger;
    }

    public void AddEntry(string content, string source, string[]? topicTags = null)
    {
        var entry = new ShortTermEntry(
            Content: content,
            Timestamp: DateTime.UtcNow,
            Source: source,
            TopicTags: topicTags ?? []);

        _mindState.AddShortTermEntry(entry);
        EvictOldEntries();

        _logger.LogDebug("Short-term memory entry added from {Source}, buffer size: {Count}",
            source, _mindState.GetShortTermMemory().Count);
    }

    public List<ShortTermEntry> GetRecentEntries(int? count = null)
    {
        var entries = _mindState.GetShortTermMemory();
        if (count.HasValue && count.Value < entries.Count)
            return entries.Skip(entries.Count - count.Value).ToList();
        return entries;
    }

    public float GetTopicConfidence(string topic)
    {
        var entries = _mindState.GetShortTermMemory();
        var cutoff = DateTime.UtcNow.AddMinutes(-_settings.ShortTermMemoryAgeMinutes);
        var recentEntries = entries.Where(e => e.Timestamp >= cutoff).ToList();

        if (recentEntries.Count == 0) return 0f;

        var mentionCount = recentEntries.Count(e =>
            e.TopicTags.Any(t => t.Equals(topic, StringComparison.OrdinalIgnoreCase)) ||
            e.Content.Contains(topic, StringComparison.OrdinalIgnoreCase));

        if (mentionCount == 0) return 0f;

        // Scale: 1 mention = 0.4 (moderate), 2 = 0.7 (confident), 3+ = 1.0
        // This maps cleanly to the prompt's confidence thresholds
        return Math.Min(1.0f, mentionCount * 0.35f + 0.05f);
    }

    private void EvictOldEntries()
    {
        var entries = _mindState.GetShortTermMemory();
        var cutoff = DateTime.UtcNow.AddMinutes(-_settings.ShortTermMemoryAgeMinutes);

        var filtered = entries
            .Where(e => e.Timestamp >= cutoff)
            .ToList();

        // Also enforce count cap
        if (filtered.Count > _settings.ShortTermMemoryCap)
            filtered = filtered.Skip(filtered.Count - _settings.ShortTermMemoryCap).ToList();

        if (filtered.Count != entries.Count)
        {
            _mindState.SetShortTermMemory(filtered);
            _logger.LogDebug("Evicted {Count} short-term memory entries", entries.Count - filtered.Count);
        }
    }
}
