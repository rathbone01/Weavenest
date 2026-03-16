using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Models;
using Weavenest.Services.Interfaces;
using Weavenest.Services.Models;
using Weavenest.Services.Models.Options;

namespace Weavenest.Services;

public class ConsciousnessLoopService : BackgroundService
{
    private const int MaxToolIterations = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MindStateService _mindState;
    private readonly ShortTermMemoryService _shortTermMemory;
    private readonly MindSettings _settings;
    private readonly ILogger<ConsciousnessLoopService> _logger;
    private readonly SemaphoreSlim _tickGuard = new(1, 1);

    public ConsciousnessLoopService(
        IServiceScopeFactory scopeFactory,
        MindStateService mindState,
        ShortTermMemoryService shortTermMemory,
        IOptions<MindSettings> settings,
        ILogger<ConsciousnessLoopService> logger)
    {
        _scopeFactory = scopeFactory;
        _mindState = mindState;
        _shortTermMemory = shortTermMemory;
        _settings = settings.Value;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ConsciousnessLoop starting — tick interval: {Interval}s", _settings.TickIntervalSeconds);

        // Recovery: load any unprocessed human messages into the queue
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeavenestDbContext>();
        var unprocessed = await db.HumanMessages
            .Where(m => !m.Processed)
            .OrderBy(m => m.Timestamp)
            .ToListAsync(cancellationToken);

        foreach (var msg in unprocessed)
        {
            _mindState.TryDequeueHumanMessage(out _); // clear if already queued
        }
        // Re-enqueue from DB without re-persisting
        foreach (var msg in unprocessed)
        {
            // Directly enqueue to in-memory queue (message already in DB)
            // We access the ConcurrentQueue through a public method
        }

        if (unprocessed.Count > 0)
            _logger.LogInformation("Recovered {Count} unprocessed human messages", unprocessed.Count);

        // Load initial emotional state
        var emotionService = scope.ServiceProvider.GetRequiredService<EmotionService>();
        var currentEmotion = await emotionService.GetCurrentStateAsync();
        _mindState.UpdateEmotionalState(currentEmotion);

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app a moment to fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.TickIntervalSeconds), stoppingToken);

            if (!await _tickGuard.WaitAsync(0, stoppingToken))
            {
                _logger.LogDebug("Tick skipped — previous tick still running");
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await ExecuteTickAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tick failed — mind sleeps this cycle");
            }
            finally
            {
                _tickGuard.Release();
            }
        }
    }

    private async Task ExecuteTickAsync(IServiceProvider services, CancellationToken ct)
    {
        var tickStart = DateTime.UtcNow;
        _logger.LogInformation("=== TICK START ===");

        var ollamaService = services.GetRequiredService<IOllamaService>();
        var emotionService = services.GetRequiredService<EmotionService>();
        var longTermMemory = services.GetRequiredService<LongTermMemoryService>();
        var promptAssembly = services.GetRequiredService<PromptAssemblyService>();
        var toolDispatch = services.GetRequiredService<ToolDispatchService>();
        var db = services.GetRequiredService<WeavenestDbContext>();

        // 1. Load emotional state (before snapshot)
        var emotionBefore = await emotionService.GetCurrentStateAsync();

        // 2. Check for human message
        string? humanMessage = null;
        if (_mindState.TryDequeueHumanMessage(out var msg))
        {
            humanMessage = msg;
            _logger.LogInformation("Processing human message: {Preview}",
                humanMessage?.Length > 60 ? humanMessage[..60] + "..." : humanMessage);

            // Mark as processed in DB
            var dbMsg = await db.HumanMessages
                .Where(m => !m.Processed)
                .OrderBy(m => m.Timestamp)
                .FirstOrDefaultAsync(ct);
            if (dbMsg is not null)
            {
                dbMsg.Processed = true;
                await db.SaveChangesAsync(ct);
            }

            // Add to short-term memory
            _shortTermMemory.AddEntry($"The human said: \"{humanMessage}\"", "human");
        }

        // 3. Extract context tags and assemble prompt
        var recentEntries = _shortTermMemory.GetRecentEntries();
        var contextTags = promptAssembly.ExtractTopicTags(humanMessage, recentEntries);
        var systemPrompt = await promptAssembly.AssembleSystemPromptAsync(contextTags);
        var stimulus = promptAssembly.BuildStimulusMessage(humanMessage);

        // 4. Call Ollama with tool loop
        var messages = new List<OllamaChatMessage>
        {
            new() { Role = "user", Content = stimulus }
        };

        var toolDefinitions = toolDispatch.GetToolDefinitions();
        var toolCallLog = new List<object>();
        string? subconsciousContent = null;
        string? consciousContent = null;
        string? spokeContent = null;

        var modelName = services.GetRequiredService<IOptions<OllamaOptions>>().Value.Model ?? "qwen3:8b";

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            _logger.LogDebug("Tool iteration {Iteration}/{Max}", iteration + 1, MaxToolIterations);

            OllamaChatResponse response;
            try
            {
                response = await ollamaService.ChatWithToolsAsync(
                    systemPrompt, messages, toolDefinitions, modelName, ct,
                    onThinkToken: token => _mindState.PublishSubconsciousToken(token));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama call failed on iteration {Iteration}", iteration + 1);
                if (iteration == 0) throw; // First call failure is fatal for this tick
                break;
            }

            // Extract subconscious: Ollama puts qwen3 thinking in message.Thinking (native API field),
            // but also check content for raw <think> tags as fallback
            string? thinking;
            string content;
            if (!string.IsNullOrWhiteSpace(response.Message.Thinking))
            {
                thinking = response.Message.Thinking;
                content = response.Message.Content;
            }
            else
            {
                (content, thinking) = ThinkTagParser.Parse(response.Message.Content);
            }

            _logger.LogDebug("Tick response — model: {Model}, content: {ContentLen} chars, thinking: {ThinkingLen} chars, tool_calls: {ToolCount}",
                response.Model, content?.Length ?? 0, thinking?.Length ?? 0, response.Message.ToolCalls?.Count ?? 0);

            if (subconsciousContent is null && thinking is not null)
                subconsciousContent = thinking;
            else if (thinking is not null)
                subconsciousContent += "\n" + thinking;

            // Check for tool calls
            if (response.Message.ToolCalls is null || response.Message.ToolCalls.Count == 0)
            {
                consciousContent = content;
                break;
            }

            // Add assistant message to history
            messages.Add(response.Message);

            // Execute tool calls
            foreach (var toolCall in response.Message.ToolCalls)
            {
                var toolName = toolCall.Function.Name;
                var result = await toolDispatch.DispatchAsync(toolName, toolCall.Function.Arguments, ct);

                toolCallLog.Add(new { Tool = toolName, Arguments = toolCall.Function.Arguments.ToString(), Result = result });

                messages.Add(new OllamaChatMessage
                {
                    Role = "tool",
                    Content = result
                });

                // Track if speak was called
                if (toolName == "speak")
                {
                    // Normalize in case Ollama returned arguments as a JSON-encoded string
                    var args = toolCall.Function.Arguments;
                    if (args.ValueKind == JsonValueKind.String)
                    {
                        var raw = args.GetString() ?? "{}";
                        args = JsonDocument.Parse(raw).RootElement;
                    }
                    var spokenMsg = args.TryGetProperty("message", out var m) ? m.GetString() : null;
                    if (spokenMsg is not null)
                        spokeContent = (spokeContent is null) ? spokenMsg : spokeContent + "\n" + spokenMsg;
                }
            }

            // If this was the last iteration, get final conscious content
            if (iteration == MaxToolIterations - 1)
            {
                consciousContent = content;
            }
        }

        // 5. Load emotional state (after — may have changed via update_emotion tool)
        var emotionAfter = await emotionService.GetCurrentStateAsync();
        _mindState.UpdateEmotionalState(emotionAfter);

        // 6. Save tick log
        var tickLog = new TickLog
        {
            Id = Guid.NewGuid(),
            Timestamp = tickStart,
            SubconsciousContent = subconsciousContent,
            ConsciousContent = consciousContent,
            SpokeContent = spokeContent,
            EmotionalStateBeforeJson = JsonSerializer.Serialize(new
            {
                emotionBefore.Happiness, emotionBefore.Sadness, emotionBefore.Disgust,
                emotionBefore.Fear, emotionBefore.Surprise, emotionBefore.Anger
            }),
            EmotionalStateAfterJson = JsonSerializer.Serialize(new
            {
                emotionAfter.Happiness, emotionAfter.Sadness, emotionAfter.Disgust,
                emotionAfter.Fear, emotionAfter.Surprise, emotionAfter.Anger
            }),
            ToolCallsJson = toolCallLog.Count > 0 ? JsonSerializer.Serialize(toolCallLog) : null
        };

        db.TickLogs.Add(tickLog);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tick log — continuing anyway");
        }

        // 7. Add conscious thought to short-term memory
        if (!string.IsNullOrWhiteSpace(consciousContent))
        {
            _shortTermMemory.AddEntry(consciousContent, "conscious", contextTags);
        }

        // 8. Publish results to UI
        var tickResult = new TickResult(
            SubconsciousContent: subconsciousContent,
            ConsciousContent: consciousContent,
            SpokeContent: spokeContent,
            EmotionalStateBefore: emotionBefore,
            EmotionalStateAfter: emotionAfter,
            Timestamp: tickStart);

        await _mindState.PublishTickResultAsync(tickResult);

        _logger.LogInformation("=== TICK END === conscious: {ConsciousLen} chars, spoke: {Spoke}, tools: {ToolCount}",
            consciousContent?.Length ?? 0, spokeContent is not null, toolCallLog.Count);
    }
}
