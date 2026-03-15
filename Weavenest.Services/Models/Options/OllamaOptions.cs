namespace Weavenest.Services.Models.Options;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public required string BaseUrl { get; set; }
    public string? DefaultModel { get; set; }
    public int DefaultContextLength { get; set; } = 2048;
}
