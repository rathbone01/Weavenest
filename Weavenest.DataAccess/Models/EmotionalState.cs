namespace Weavenest.DataAccess.Models;

public class EmotionalState
{
    public Guid Id { get; set; }
    public float Happiness { get; set; }
    public float Sadness { get; set; }
    public float Disgust { get; set; }
    public float Fear { get; set; }
    public float Surprise { get; set; }
    public float Anger { get; set; }
    public DateTime Timestamp { get; set; }
}
