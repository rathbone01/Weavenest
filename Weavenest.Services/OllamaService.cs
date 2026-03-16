using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using Weavenest.Services.Interfaces;
using Weavenest.Services.Models;
using Weavenest.Services.Models.Options;

namespace Weavenest.Services;

public class OllamaService : IOllamaService
{
    private readonly OllamaApiClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaService> _logger;
    private readonly OllamaOptions _options;
    private readonly ConcurrentDictionary<string, bool> _thinkingCapabilityCache = new(StringComparer.OrdinalIgnoreCase);

    public OllamaService(
        IOptions<OllamaOptions> config,
        IHttpClientFactory httpClientFactory,
        ILogger<OllamaService> logger)
    {
        _options = config.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        var uri = new Uri(_options.BaseUrl);
        _client = new OllamaApiClient(uri, _options.Model ?? "");
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        IList<OllamaChatMessage> history,
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

        var supportsThinking = await SupportsThinkingAsync(modelName, cancellationToken);
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

    public async Task<OllamaChatResponse> ChatWithToolsAsync(
        string systemPrompt,
        List<OllamaChatMessage> messages,
        List<OllamaTool>? tools,
        string modelName,
        CancellationToken ct = default,
        Action<string>? onThinkToken = null)
    {
        var client = _httpClientFactory.CreateClient("OllamaApi");

        var allMessages = new List<OllamaChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };
        allMessages.AddRange(messages);

        // Always non-streaming so Ollama properly parses tool_calls from the complete response.
        // think:true tells Ollama to extract <think> blocks into message.thinking even in non-streaming mode.
        var chatRequest = new OllamaChatRequest
        {
            Model = modelName,
            Messages = allMessages,
            Stream = false,
            Think = true,
            Tools = tools
        };

        var json = JsonSerializer.Serialize(chatRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"{_options.BaseUrl.TrimEnd('/')}/api/chat";

        _logger.LogInformation("ChatWithTools — model: {Model}, messages: {Count}, tools: {ToolCount}",
            modelName, allMessages.Count, tools?.Count ?? 0);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(url, content, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Ollama at {Url}", url);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var statusCode = (int)response.StatusCode;
            _logger.LogError("Ollama API error {StatusCode}: {Body}", statusCode, errorBody);
            throw new HttpRequestException($"Ollama API error {statusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<OllamaChatResponse>(responseJson);
        return result ?? throw new InvalidOperationException("Ollama returned an empty or unparseable response");
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
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private async Task<bool> SupportsThinkingAsync(string modelName, CancellationToken cancellationToken = default)
    {
        if (_thinkingCapabilityCache.TryGetValue(modelName, out var cached))
            return cached;

        try
        {
            var response = await _client.ShowModelAsync(new ShowModelRequest { Model = modelName }, cancellationToken);
            var supports = response.Capabilities?.Contains("thinking", StringComparer.OrdinalIgnoreCase) ?? false;
            _thinkingCapabilityCache[modelName] = supports;
            _logger.LogInformation("Model {Model} thinking support: {Supports}", modelName, supports);
            return supports;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check capabilities for model {Model}, defaulting to no thinking", modelName);
            _thinkingCapabilityCache[modelName] = false;
            return false;
        }
    }

    private static OllamaSharp.Models.Chat.ChatRole MapRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => OllamaSharp.Models.Chat.ChatRole.System,
        "user" => OllamaSharp.Models.Chat.ChatRole.User,
        "assistant" => OllamaSharp.Models.Chat.ChatRole.Assistant,
        "tool" => OllamaSharp.Models.Chat.ChatRole.Tool,
        _ => OllamaSharp.Models.Chat.ChatRole.User
    };
}
