namespace Weavenest.DataAccess.Models;

public class HumanMessage
{
    public Guid Id { get; set; }
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public bool Processed { get; set; }
}
