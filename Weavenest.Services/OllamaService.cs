using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using Weavenest.DataAccess.Models;
using Weavenest.Services.Interfaces;
using Weavenest.Services.Models.Options;

namespace Weavenest.Services;

public class OllamaService : IOllamaService
{
    private readonly OllamaApiClient _client;
    private readonly ILogger<OllamaService> _logger;
    private readonly OllamaOptions _options;
    private readonly ConcurrentDictionary<string, ModelCapabilities> _capabilitiesCache = new(StringComparer.OrdinalIgnoreCase);

    public OllamaService(IOptions<OllamaOptions> config, ILogger<OllamaService> logger)
    {
        _options = config.Value;
        _logger = logger;
        var uri = new Uri(_options.BaseUrl);
        _client = new OllamaApiClient(uri, _options.Model ?? "");
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        IList<ChatMessage> history,
        string userMessage,
        string modelName,
        string? systemPrompt = null,
        Action<string>? onThinkToken = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting chat stream — model: {Model}, history: {HistoryCount} messages, system prompt: {HasSystemPrompt}",
            modelName, history.Count, systemPrompt is not null);

        var chat = systemPrompt is not null
            ? new Chat(_client, systemPrompt.Replace("{{currentDateTime}}", DateTime.Now.ToString()))
            : new Chat(_client);

        chat.Model = modelName;

        var capabilities = await GetModelCapabilitiesAsync(modelName, cancellationToken);
        var supportsThinking = capabilities.SupportsThinking;
        if (supportsThinking)
        {
            chat.Think = true;
            chat.OnThink += (_, token) => onThinkToken?.Invoke(token ?? "");
        }

        foreach (var msg in history)
        {
            chat.Messages.Add(new OllamaSharp.Models.Chat.Message
            {
                Role = MapRole(msg.Role),
                Content = msg.Content
            });
        }

        var tokenCount = 0;
        await foreach (var token in chat.SendAsync(userMessage, cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Chat stream cancelled after {TokenCount} tokens — model: {Model}", tokenCount, modelName);
                yield break;
            }
            tokenCount++;
            yield return token ?? string.Empty;
        }

        _logger.LogInformation("Chat stream complete — model: {Model}, tokens yielded: {TokenCount}", modelName, tokenCount);
    }

    public async Task<IEnumerable<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await _client.ListLocalModelsAsync(cancellationToken);
            var names = models.Select(m => m.Name).ToList();

            // Check capabilities in parallel so the cache is warm and embedding models are excluded
            var capabilityTasks = names.Select(name => GetModelCapabilitiesAsync(name, cancellationToken));
            var capabilities = await Task.WhenAll(capabilityTasks);

            return names
                .Zip(capabilities, (name, caps) => (name, caps))
                .Where(x => !x.caps.IsEmbeddingOnly)
                .Select(x => x.name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Ollama models");
            return [];
        }
    }

    public async Task<ModelContextInfo> GetModelContextInfoAsync(
        string modelName, CancellationToken cancellationToken = default)
    {
        try
        {
            var runningModels = await _client.ListRunningModelsAsync(cancellationToken);
            var running = runningModels.FirstOrDefault(m =>
                m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));

            if (running is not null)
            {
                var contextLen = running.ContextLength > 0
                    ? running.ContextLength
                    : _options.DefaultContextLength;
                return new ModelContextInfo(
                    modelName,
                    contextLen,
                    IsRunning: true);
            }

            return new ModelContextInfo(modelName, _options.DefaultContextLength, IsRunning: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get context info for model {Model}", modelName);
            return new ModelContextInfo(modelName, _options.DefaultContextLength, IsRunning: false);
        }
    }

    public int EstimateTokenCount(string text)
    {
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    public async Task<ModelCapabilities> GetModelCapabilitiesAsync(string modelName, CancellationToken cancellationToken = default)
    {
        if (_capabilitiesCache.TryGetValue(modelName, out var cached))
            return cached;

        try
        {
            var response = await _client.ShowModelAsync(new ShowModelRequest { Model = modelName }, cancellationToken);
            var caps = response.Capabilities ?? [];
            var supportsThinking = caps.Contains("thinking", StringComparer.OrdinalIgnoreCase);
            var supportsTools = caps.Contains("tools", StringComparer.OrdinalIgnoreCase);
            var isEmbeddingOnly = caps.Contains("embedding", StringComparer.OrdinalIgnoreCase)
                                  && !caps.Contains("completion", StringComparer.OrdinalIgnoreCase);
            var result = new ModelCapabilities(supportsThinking, supportsTools, isEmbeddingOnly);
            _capabilitiesCache[modelName] = result;
            _logger.LogInformation("Model {Model} capabilities — thinking: {Thinking}, tools: {Tools}, embeddingOnly: {EmbeddingOnly}",
                modelName, supportsThinking, supportsTools, isEmbeddingOnly);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check capabilities for model {Model}, defaulting to none", modelName);
            var fallback = new ModelCapabilities(SupportsThinking: false, SupportsTools: false, IsEmbeddingOnly: false);
            _capabilitiesCache[modelName] = fallback;
            return fallback;
        }
    }

    private static OllamaSharp.Models.Chat.ChatRole MapRole(DataAccess.Models.ChatRole role) => role switch
    {
        DataAccess.Models.ChatRole.System => OllamaSharp.Models.Chat.ChatRole.System,
        DataAccess.Models.ChatRole.User => OllamaSharp.Models.Chat.ChatRole.User,
        DataAccess.Models.ChatRole.Assistant => OllamaSharp.Models.Chat.ChatRole.Assistant,
        _ => OllamaSharp.Models.Chat.ChatRole.User
    };
}
