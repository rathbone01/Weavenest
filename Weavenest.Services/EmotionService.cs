using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Models;
using Weavenest.Services.Models.Options;

namespace Weavenest.Services;

public class EmotionService
{
    private readonly WeavenestDbContext _db;
    private readonly MindSettings _settings;
    private readonly ILogger<EmotionService> _logger;

    public EmotionService(
        WeavenestDbContext db,
        IOptions<MindSettings> settings,
        ILogger<EmotionService> logger)
    {
        _db = db;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<EmotionalState> GetCurrentStateAsync()
    {
        var latest = await _db.EmotionalStates
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync();

        if (latest is not null)
            return latest;

        // Create default neutral state
        var defaultState = new EmotionalState
        {
            Id = Guid.NewGuid(),
            Happiness = 0.5f,
            Sadness = 0.1f,
            Disgust = 0.05f,
            Fear = 0.1f,
            Surprise = 0.3f,
            Anger = 0.1f,
            Timestamp = DateTime.UtcNow
        };

        _db.EmotionalStates.Add(defaultState);
        await _db.SaveChangesAsync();
        return defaultState;
    }

    public async Task<EmotionalState> ApplyDeltaAsync(Dictionary<string, float> deltas)
    {
        var current = await GetCurrentStateAsync();
        var maxDelta = _settings.MaxEmotionDeltaPerTick;

        var newState = new EmotionalState
        {
            Id = Guid.NewGuid(),
            Happiness = ApplyClampedDelta(current.Happiness, GetDelta(deltas, "happiness"), maxDelta),
            Sadness = ApplyClampedDelta(current.Sadness, GetDelta(deltas, "sadness"), maxDelta),
            Disgust = ApplyClampedDelta(current.Disgust, GetDelta(deltas, "disgust"), maxDelta),
            Fear = ApplyClampedDelta(current.Fear, GetDelta(deltas, "fear"), maxDelta),
            Surprise = ApplyClampedDelta(current.Surprise, GetDelta(deltas, "surprise"), maxDelta),
            Anger = ApplyClampedDelta(current.Anger, GetDelta(deltas, "anger"), maxDelta),
            Timestamp = DateTime.UtcNow
        };

        _db.EmotionalStates.Add(newState);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Emotional state updated: H={H:F2} S={S:F2} D={D:F2} F={F:F2} Su={Su:F2} A={A:F2}",
            newState.Happiness, newState.Sadness, newState.Disgust,
            newState.Fear, newState.Surprise, newState.Anger);

        return newState;
    }

    public string DescribeState(EmotionalState state)
    {
        var descriptions = new List<string>();

        AddDescription(descriptions, state.Happiness, "happy", "content", "joyful");
        AddDescription(descriptions, state.Sadness, "sad", "melancholy", "deeply sorrowful");
        AddDescription(descriptions, state.Anger, "irritated", "angry", "furious");
        AddDescription(descriptions, state.Fear, "uneasy", "anxious", "afraid");
        AddDescription(descriptions, state.Disgust, "mildly put off", "disgusted", "revolted");
        AddDescription(descriptions, state.Surprise, "slightly curious", "surprised", "astonished");

        if (descriptions.Count == 0)
            return "You feel emotionally neutral.";

        return $"You are currently feeling {string.Join(", ", descriptions)}.";
    }

    public async Task<List<EmotionalState>> GetHistoryAsync(int count)
    {
        return await _db.EmotionalStates
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    private static float ApplyClampedDelta(float current, float delta, float maxDelta)
    {
        var clampedDelta = Math.Clamp(delta, -maxDelta, maxDelta);
        return Math.Clamp(current + clampedDelta, 0f, 1f);
    }

    private static float GetDelta(Dictionary<string, float> deltas, string key)
    {
        return deltas.TryGetValue(key, out var val) ? val : 0f;
    }

    private static void AddDescription(List<string> descriptions, float value, string low, string mid, string high)
    {
        if (value > 0.7f)
            descriptions.Add(high);
        else if (value > 0.4f)
            descriptions.Add(mid);
        else if (value > 0.2f)
            descriptions.Add(low);
    }
}
