using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Weavenest.DataAccess.Models;
using Weavenest.Services.Interfaces;
using Weavenest.Services.Models.Options;

namespace Weavenest.Services;

public class OllamaService : IOllamaService
{
    private readonly OllamaApiClient _client;
    private readonly ILogger<OllamaService> _logger;
    private readonly OllamaOptions _options;

    public OllamaService(IOptions<OllamaOptions> config, ILogger<OllamaService> logger)
    {
        _options = config.Value;
        _logger = logger;
        var uri = new Uri(_options.BaseUrl);
        _client = new OllamaApiClient(uri, _options.DefaultModel ?? "");
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        IList<ChatMessage> history,
        string userMessage,
        string modelName,
        string? systemPrompt = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting chat stream — model: {Model}, history: {HistoryCount} messages, system prompt: {HasSystemPrompt}",
            modelName, history.Count, systemPrompt is not null);

        var chat = systemPrompt is not null
            ? new Chat(_client, systemPrompt.Replace("{{currentDateTime}}", DateTime.Now.ToString()))
            : new Chat(_client);

        chat.Model = modelName;

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
            return models.Select(m => m.Name);
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
        // ~4 characters per token is a standard heuristic for English text
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private static OllamaSharp.Models.Chat.ChatRole MapRole(DataAccess.Models.ChatRole role) => role switch
    {
        DataAccess.Models.ChatRole.System => OllamaSharp.Models.Chat.ChatRole.System,
        DataAccess.Models.ChatRole.User => OllamaSharp.Models.Chat.ChatRole.User,
        DataAccess.Models.ChatRole.Assistant => OllamaSharp.Models.Chat.ChatRole.Assistant,
        _ => OllamaSharp.Models.Chat.ChatRole.User
    };
}
