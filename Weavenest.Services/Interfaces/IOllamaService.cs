using Weavenest.Services.Models;

namespace Weavenest.Services.Interfaces;

public interface IOllamaService
{
    IAsyncEnumerable<string> ChatStreamAsync(
        IList<OllamaChatMessage> history,
        string userMessage,
        string modelName,
        string? systemPrompt = null,
        Action<string>? onThinkToken = null,
        CancellationToken cancellationToken = default);

    Task<OllamaChatResponse> ChatWithToolsAsync(
        string systemPrompt,
        List<OllamaChatMessage> messages,
        List<OllamaTool>? tools,
        string modelName,
        CancellationToken ct = default,
        Action<string>? onThinkToken = null);

    Task<IEnumerable<string>> GetModelsAsync(CancellationToken cancellationToken = default);

    Task<ModelContextInfo> GetModelContextInfoAsync(string modelName, CancellationToken cancellationToken = default);

    int EstimateTokenCount(string text);
}

public record ModelContextInfo(
    string ModelName,
    int ContextLength,
    bool IsRunning);
