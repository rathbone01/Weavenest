using System.Text.Json.Serialization;

namespace Weavenest.Services.Models;

public class OllamaEmbedRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("input")]
    public string Input { get; set; } = "";
}

public class OllamaEmbedResponse
{
    [JsonPropertyName("embeddings")]
    public List<float[]> Embeddings { get; set; } = [];
}
