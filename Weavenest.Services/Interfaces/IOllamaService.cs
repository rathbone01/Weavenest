using Weavenest.DataAccess.Models;

namespace Weavenest.Services.Interfaces;

public interface IOllamaService
{
    IAsyncEnumerable<string> ChatStreamAsync(
        IList<ChatMessage> history,
        string userMessage,
        string modelName,
        string? systemPrompt = null,
        Action<string>? onThinkToken = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<string>> GetModelsAsync(CancellationToken cancellationToken = default);

    Task<ModelContextInfo> GetModelContextInfoAsync(string modelName, CancellationToken cancellationToken = default);

    Task<ModelCapabilities> GetModelCapabilitiesAsync(string modelName, CancellationToken cancellationToken = default);

    int EstimateTokenCount(string text);
}

public record ModelContextInfo(
    string ModelName,
    int ContextLength,
    bool IsRunning);

public record ModelCapabilities(
    bool SupportsThinking,
    bool SupportsTools,
    bool IsEmbeddingOnly);
