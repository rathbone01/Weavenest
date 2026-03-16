namespace Weavenest.Services.Models.Options;

public class MindSettings
{
    public const string SectionName = "MindSettings";

    public int TickIntervalSeconds { get; set; } = 30;
    public int ShortTermMemoryCap { get; set; } = 50;
    public int ShortTermMemoryAgeMinutes { get; set; } = 60;
    public float MaxEmotionDeltaPerTick { get; set; } = 0.1f;
    public int LongTermMemoryRetrievalCount { get; set; } = 10;
    public float RecencyWeight { get; set; } = 0.6f;
    public float RelevanceWeight { get; set; } = 0.4f;
    public float ConfidenceLowThreshold { get; set; } = 0.4f;
    public float ConfidenceHighThreshold { get; set; } = 0.7f;
}
