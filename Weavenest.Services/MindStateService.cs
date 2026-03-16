using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Weavenest.DataAccess.Data;
using Weavenest.DataAccess.Models;

namespace Weavenest.Services;

public record ShortTermEntry(
    string Content,
    DateTime Timestamp,
    string Source,
    string[] TopicTags);

public record ToolCallRecord(
    string ToolName,
    string Arguments,
    string Result,
    bool Succeeded,
    DateTime Timestamp);

public record TickResult(
    string? SubconsciousContent,
    string? ConsciousContent,
    string? SpokeContent,
    EmotionalState EmotionalStateBefore,
    EmotionalState EmotionalStateAfter,
    DateTime Timestamp);

public class MindStateService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MindStateService> _logger;
    private readonly object _lock = new();

    private EmotionalState _currentEmotionalState = new()
    {
        Id = Guid.NewGuid(),
        Happiness = 0.5f,
        Sadness = 0.1f,
        Disgust = 0.05f,
        Fear = 0.1f,
        Surprise = 0.3f,
        Anger = 0.1f,
        Timestamp = DateTime.UtcNow
    };

    // Volatile so ConsciousnessLoopService can read it from a background thread without a lock.
    // A single bool write is inherently atomic; volatile just ensures CPU cache visibility.
    private volatile bool _isUserTyping;

    private readonly List<ShortTermEntry> _shortTermMemory = [];
    private readonly ConcurrentQueue<string> _pendingHumanMessages = new();
    private TickResult? _latestTickResult;
    private readonly List<string> _spokenMessages = [];
    private readonly List<string> _humanMessages = [];
    private readonly List<ToolCallRecord> _toolCallLog = [];

    public event Func<Task>? OnTickCompleted;
    public event Func<Task>? OnEmotionChanged;
    public event Func<string, Task>? OnSpoke;
    public event Func<string, Task>? OnConsciousThought;
    public event Func<string, Task>? OnSubconsciousToken;
    public event Func<ToolCallRecord, Task>? OnToolCall;

    public MindStateService(IServiceScopeFactory scopeFactory, ILogger<MindStateService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public EmotionalState GetEmotionalState()
    {
        lock (_lock) return _currentEmotionalState;
    }

    public void UpdateEmotionalState(EmotionalState state)
    {
        lock (_lock) _currentEmotionalState = state;
        _ = NotifyAsync(OnEmotionChanged);
    }

    public List<ShortTermEntry> GetShortTermMemory()
    {
        lock (_lock) return [.. _shortTermMemory];
    }

    public void AddShortTermEntry(ShortTermEntry entry)
    {
        lock (_lock) _shortTermMemory.Add(entry);
    }

    public void SetShortTermMemory(List<ShortTermEntry> entries)
    {
        lock (_lock)
        {
            _shortTermMemory.Clear();
            _shortTermMemory.AddRange(entries);
        }
    }

    public bool IsUserTyping => _isUserTyping;

    /// <summary>Called by the UI when the user starts or stops composing a message.</summary>
    public void SetUserTyping(bool typing) {
    _isUserTyping = typing;
    }

    public bool TryDequeueHumanMessage(out string? message)
    {
        return _pendingHumanMessages.TryDequeue(out message);
    }

    public async Task EnqueueHumanMessageAsync(string content)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeavenestDbContext>();

        var msg = new HumanMessage
        {
            Id = Guid.NewGuid(),
            Content = content,
            Timestamp = DateTime.UtcNow,
            Processed = false
        };
        db.HumanMessages.Add(msg);
        await db.SaveChangesAsync();

        _pendingHumanMessages.Enqueue(content);

        lock (_lock) _humanMessages.Add(content);

        _logger.LogInformation("Human message enqueued: {Preview}",
            content.Length > 80 ? content[..80] + "..." : content);
    }

    public void PublishSpokenMessage(string message)
    {
        lock (_lock) _spokenMessages.Add(message);
        _ = NotifyAsync(OnSpoke, message);
    }

    public void RecordToolCall(ToolCallRecord record)
    {
        lock (_lock)
        {
            _toolCallLog.Add(record);
            if (_toolCallLog.Count > 200)
                _toolCallLog.RemoveRange(0, _toolCallLog.Count - 200);
        }
        _ = NotifyAsync(OnToolCall, record);
    }

    public List<ToolCallRecord> GetToolCallLog()
    {
        lock (_lock) return [.. _toolCallLog];
    }

    public List<string> GetSpokenMessages()
    {
        lock (_lock) return [.. _spokenMessages];
    }

    public List<string> GetHumanMessages()
    {
        lock (_lock) return [.. _humanMessages];
    }

    public TickResult? GetLatestTickResult()
    {
        lock (_lock) return _latestTickResult;
    }

    public void PublishSubconsciousToken(string token)
    {
        _ = NotifyAsync(OnSubconsciousToken, token);
    }

    public async Task PublishTickResultAsync(TickResult result)
    {
        lock (_lock) _latestTickResult = result;

        if (result.ConsciousContent is not null)
            await NotifyAsync(OnConsciousThought, result.ConsciousContent);

        await NotifyAsync(OnTickCompleted);
    }

    private async Task NotifyAsync(Func<Task>? handler)
    {
        if (handler is null) return;
        try { await handler(); }
        catch (Exception ex) { _logger.LogError(ex, "Error in event handler"); }
    }

    private async Task NotifyAsync(Func<string, Task>? handler, string arg)
    {
        if (handler is null) return;
        try { await handler(arg); }
        catch (Exception ex) { _logger.LogError(ex, "Error in event handler"); }
    }

    private async Task NotifyAsync(Func<ToolCallRecord, Task>? handler, ToolCallRecord arg)
    {
        if (handler is null) return;
        try { await handler(arg); }
        catch (Exception ex) { _logger.LogError(ex, "Error in event handler"); }
    }
}
