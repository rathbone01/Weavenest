namespace Weavenest.DataAccess.Models;

public class LongTermMemory
{
    public Guid Id { get; set; }
    public MemoryCategory Category { get; set; }
    public string Content { get; set; } = "";
    public string TagsJson { get; set; } = "[]";
    public int Importance { get; set; } = 3;
    public float Confidence { get; set; } = 0.5f;
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public string LinkedMemoryIdsJson { get; set; } = "[]";
    public string? EmotionalContextJson { get; set; }
    public bool IsSuperseded { get; set; }
}
