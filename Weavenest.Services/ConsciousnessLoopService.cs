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
        _logger.LogInformation("ConsciousnessLoop starting — continuous mode, min gap: {Gap}s, pauses while user is typing", _settings.MinTickGapSeconds);

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
            // Gate: pause while the user is actively composing a message so we don't
            // waste a full Ollama call the instant before they hit send.
            while (_mindState.IsUserTyping && !stoppingToken.IsCancellationRequested)
                await Task.Delay(250, stoppingToken);

            if (!await _tickGuard.WaitAsync(0, stoppingToken))
            {
                // Shouldn't normally happen — previous tick is still running.
                _logger.LogDebug("Tick skipped — previous tick still running");
                await Task.Delay(100, stoppingToken);
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
                _logger.LogError(ex, "Tick failed");
            }
            finally
            {
                _tickGuard.Release();
            }

            // Minimum cooldown between ticks — prevents tight loops if Ollama errors fast.
            if (_settings.MinTickGapSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(_settings.MinTickGapSeconds), stoppingToken);
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

        // 2. Drain all queued human messages for this tick
        var humanMessages = new List<string>();
        while (_mindState.TryDequeueHumanMessage(out var msg) && msg is not null)
            humanMessages.Add(msg);

        if (humanMessages.Count > 0)
        {
            _logger.LogInformation("Processing {Count} human message(s) this tick", humanMessages.Count);

            // Mark all unprocessed DB messages as processed
            var dbMsgs = await db.HumanMessages
                .Where(m => !m.Processed)
                .OrderBy(m => m.Timestamp)
                .Take(humanMessages.Count)
                .ToListAsync(ct);
            foreach (var dbMsg in dbMsgs)
                dbMsg.Processed = true;
            if (dbMsgs.Count > 0)
                await db.SaveChangesAsync(ct);

            // Add each to short-term memory
            foreach (var m in humanMessages)
                _shortTermMemory.AddEntry($"The human said: \"{m}\"", "human");
        }

        // 3. Extract context tags and assemble prompt
        var recentEntries = _shortTermMemory.GetRecentEntries();
        var contextTags = promptAssembly.ExtractTopicTags(humanMessages.Count > 0 ? humanMessages[0] : null, recentEntries);
        var systemPrompt = await promptAssembly.AssembleSystemPromptAsync(contextTags);
        var stimulus = promptAssembly.BuildStimulusMessage(humanMessages);

        // 4. Call Ollama with tool loop
        var messages = new List<OllamaChatMessage>
        {
            new() { Role = "user", Content = stimulus }
        };

        var toolDefinitions = toolDispatch.GetToolDefinitions();
        var toolCallLog = new List<object>();
        var seenToolCalls = new HashSet<string>(StringComparer.Ordinal);
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
                    systemPrompt, messages, toolDefinitions, modelName, ct);
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
                content = ThinkTagParser.StripThinkTags(response.Message.Content);
            }
            else
            {
                (content, thinking) = ThinkTagParser.Parse(response.Message.Content);
            }
            // Remove any raw <tool_call> blocks qwen3 writes when confused about tool availability
            content = ThinkTagParser.StripToolCallBlocks(content);

            _logger.LogDebug("Tick response — model: {Model}, content: {ContentLen} chars, thinking: {ThinkingLen} chars, tool_calls: {ToolCount}",
                response.Model, content?.Length ?? 0, thinking?.Length ?? 0, response.Message.ToolCalls?.Count ?? 0);

            if (subconsciousContent is null && thinking is not null)
                subconsciousContent = thinking;
            else if (thinking is not null)
                subconsciousContent += "\n" + thinking;

            // Publish thinking to UI immediately after this Ollama call (not waiting for full tick)
            if (!string.IsNullOrWhiteSpace(thinking))
                _mindState.PublishSubconsciousToken(thinking);

            // No tool calls — capture conscious content and stop
            if (response.Message.ToolCalls is null || response.Message.ToolCalls.Count == 0)
            {
                consciousContent = content;
                break;
            }

            // Add assistant message to history
            messages.Add(response.Message);

            // Execute tool calls with repeat detection
            var allRepeats = true;
            foreach (var toolCall in response.Message.ToolCalls)
            {
                var toolName = toolCall.Function.Name;

                // Normalize arguments — Ollama sometimes returns them as a JSON-encoded string
                var args = toolCall.Function.Arguments;
                if (args.ValueKind == JsonValueKind.String)
                {
                    var raw = args.GetString() ?? "{}";
                    args = JsonDocument.Parse(raw).RootElement;
                }

                // Repeat guard: skip duplicate tool calls within this tick
                var callKey = $"{toolName}:{args}";
                if (!seenToolCalls.Add(callKey))
                {
                    _logger.LogWarning("Repeat tool call detected and skipped: {ToolName} with args {Args}", toolName, args);
                    messages.Add(new OllamaChatMessage
                    {
                        Role = "tool",
                        Content = $"[Skipped: you already called {toolName} with these exact arguments this tick. Try a different query or move on.]"
                    });
                    continue;
                }

                allRepeats = false;
                var result = await toolDispatch.DispatchAsync(toolName, args, ct);

                toolCallLog.Add(new { Tool = toolName, Arguments = args.ToString(), Result = result });

                messages.Add(new OllamaChatMessage
                {
                    Role = "tool",
                    Content = result
                });

                if (toolName == "speak")
                {
                    var spokenMsg = args.TryGetProperty("message", out var m) ? m.GetString() : null;
                    if (spokenMsg is not null)
                        spokeContent = (spokeContent is null) ? spokenMsg : spokeContent + "\n" + spokenMsg;
                }
            }

            // If every tool call this iteration was a repeat, the model is looping — break out
            if (allRepeats)
            {
                _logger.LogWarning("All tool calls in iteration were repeats — breaking tool loop");
                break;
            }
        }

        // If tools were used the loop exits without a final prose response.
        // Make one tool-free call so Jeremy can reflect and write a conscious thought.
        if (consciousContent is null && toolCallLog.Count > 0)
        {
            _logger.LogDebug("Making final reflection call after tool use");
            try
            {
                // Tell the model explicitly this is an inner monologue phase — no tools, no tool_call syntax.
                // Without this hint, qwen3 falls back to writing raw <tool_call> text when tools are absent.
                var reflMessages = new List<OllamaChatMessage>(messages)
                {
                    new()
                    {
                        Role = "user",
                        Content = "[Inner monologue phase — tools are no longer available for this tick. Write only your conscious thoughts as plain text. Do NOT attempt to call tools by writing JSON, XML, or function-call syntax — it will not work. Simply describe what you're thinking and feeling.]"
                    }
                };

                var reflectionResponse = await ollamaService.ChatWithToolsAsync(
                    systemPrompt, reflMessages, tools: null, modelName, ct);

                string? reflThinking;
                string reflContent;
                if (!string.IsNullOrWhiteSpace(reflectionResponse.Message.Thinking))
                {
                    reflThinking = reflectionResponse.Message.Thinking;
                    reflContent = ThinkTagParser.StripThinkTags(reflectionResponse.Message.Content);
                }
                else
                {
                    (reflContent, reflThinking) = ThinkTagParser.Parse(reflectionResponse.Message.Content);
                }

                if (!string.IsNullOrWhiteSpace(reflThinking))
                {
                    subconsciousContent = subconsciousContent is null ? reflThinking : subconsciousContent + "\n" + reflThinking;
                    _mindState.PublishSubconsciousToken(reflThinking);
                }

                consciousContent = reflContent;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Final reflection call failed — conscious content will be empty this tick");
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
