namespace Weavenest.Services.Models.Options;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public required string BaseUrl { get; set; }
    public string? Model { get; set; }
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public int DefaultContextLength { get; set; } = 2048;
}
