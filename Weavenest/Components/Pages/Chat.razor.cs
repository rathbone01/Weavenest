using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using Weavenest.Components.Chat;
using Weavenest.DataAccess.Models;
using Weavenest.DataAccess.Repositories;
using Weavenest.Services;
using Weavenest.Services.Interfaces;

namespace Weavenest.Components.Pages;

public partial class Chat : IDisposable
{
    [Parameter] public Guid? SessionId { get; set; }

    private bool IsStreamingThisSession => _isStreaming && _currentSession?.Id == _streamingSessionId;

    private string InputAdornmentIcon => IsStreamingThisSession
        ? Icons.Material.Filled.Stop
        : Icons.Material.Filled.Send;

    private Color InputAdornmentColor => IsStreamingThisSession
        ? Color.Error
        : _isStreaming
            ? Color.Default
            : Color.Primary;

    private string InputAdornmentLabel => IsStreamingThisSession ? "Stop" : "Send";

    private async Task HandleAdornmentClick()
    {
        if (IsStreamingThisSession)
            _streamingCts?.Cancel();
        else
            await SendMessage();
    }

    private string SystemPromptTooltip => Settings.SystemPrompt is not null
        ? $"System prompt active: \"{Settings.SystemPrompt[..Math.Min(40, Settings.SystemPrompt.Length)]}...\""
        : "Configure system prompt";

    private ChatSession? _currentSession;
    private List<string> _availableModels = [];
    private string _selectedModel = "";
    private string _userInput = "";
    private bool _isStreaming;
    private Guid? _streamingSessionId;
    private ChatSession? _streamingSession;
    private ChatMessage _streamingMessage = new()
    {
        Role = ChatRole.Assistant,
        Content = ""
    };
    private bool _inThinkBlock;
    private string _tokenBuffer = "";
    private int _estimatedTokenCount;
    private int _contextLength = 2048;
    private CancellationTokenSource? _streamingCts;
    private ElementReference _messagesContainer;

    protected override async Task OnInitializedAsync()
    {
        await LoadModels();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (SessionId.HasValue)
        {
            // Skip reloading if we're navigating back to the session that is currently
            // streaming — a DB snapshot would overwrite in-flight messages.
            // Restore the in-memory session reference so the UI shows it.
            if (_isStreaming && SessionId.Value == _streamingSessionId)
            {
                _currentSession = _streamingSession;
                return;
            }

            _currentSession = await ChatRepo.GetSessionByIdAsync(SessionId.Value);
            if (_currentSession is not null)
            {
                _selectedModel = _currentSession.ModelName ?? _selectedModel;
                UpdateTokenEstimate();
                await UpdateContextInfo();
            }
        }
        else
        {
            _currentSession = null;
            _estimatedTokenCount = 0;
        }
    }

    private async Task LoadModels()
    {
        var models = await OllamaService.GetModelsAsync();
        _availableModels = models.ToList();
        if (_availableModels.Count > 0 && string.IsNullOrEmpty(_selectedModel))
        {
            _selectedModel = _availableModels[0];
        }
        await UpdateContextInfo();
    }

    private async Task OnModelChanged(string newModel)
    {
        _selectedModel = newModel;
        await UpdateContextInfo();
    }

    private async Task UpdateContextInfo()
    {
        if (!string.IsNullOrEmpty(_selectedModel))
        {
            var info = await OllamaService.GetModelContextInfoAsync(_selectedModel);
            _contextLength = info.ContextLength;
        }
    }

    private async Task OpenSettingsDialog()
    {
        var parameters = new DialogParameters
        {
            ["SystemPrompt"] = Settings.SystemPrompt
        };

        var options = new DialogOptions
        {
            CloseButton = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true
        };

        var dialog = await DialogService.ShowAsync<SystemPromptDialog>("System Prompt", parameters, options);
        var result = await dialog.Result;

        if (!result!.Canceled)
        {
            Settings.SystemPrompt = result.Data as string;
        }
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey && !_isStreaming)
        {
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(_userInput) || _isStreaming)
            return;

        if (string.IsNullOrEmpty(_selectedModel))
            return;

        var userText = _userInput.Trim();
        _userInput = "";

        if (_currentSession is null)
        {
            var userId = await UserIdentity.GetCurrentUserIdAsync();
            if (userId is null)
                return;

            var title = userText.Length > 50
                ? userText[..50] + "..."
                : userText;
            _currentSession = await ChatRepo.CreateSessionAsync(userId.Value, title, _selectedModel);
            await StateNotifier.NotifySessionsChanged();
            Navigation.NavigateTo($"/chat/{_currentSession.Id}", replace: true);
        }

        // Capture session and model now — navigation can change _currentSession/_selectedModel mid-stream
        var activeSession = _currentSession;
        var activeModel = _selectedModel;

        // Snapshot history BEFORE adding the new user message — SendAsync will append it
        var history = activeSession.Messages
            .Where(m => m.Role != ChatRole.Assistant || m.Content.Length > 0)
            .ToList();

        var userMsg = await ChatRepo.AddMessageAsync(activeSession.Id, ChatRole.User, userText);
        activeSession.Messages.Add(userMsg);

        _isStreaming = true;
        _streamingSessionId = activeSession.Id;
        _streamingSession = activeSession;
        _inThinkBlock = false;
        _tokenBuffer = "";
        _streamingMessage = new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "",
            ModelName = activeModel
        };
        _streamingCts = new CancellationTokenSource();
        StateHasChanged();

        try
        {

            await foreach (var token in OllamaService.ChatStreamAsync(
                history, userText, activeModel,
                systemPrompt: Settings.SystemPrompt,
                onThinkToken: t =>
                {
                    _streamingMessage.Thinking = (_streamingMessage.Thinking ?? "") + t;
                    InvokeAsync(StateHasChanged);
                },
                cancellationToken: _streamingCts.Token))
            {
                RouteStreamingToken(token);
                StateHasChanged();

                try
                {
                    await JSRuntime.InvokeVoidAsync("chatInterop.scrollToBottom", _messagesContainer);
                }
                catch
                {
                    // JS interop may fail during disconnect
                }
            }

            // Flush any remaining characters held by the lookahead buffer
            if (_tokenBuffer.Length > 0)
            {
                if (_inThinkBlock)
                    _streamingMessage.Thinking = (_streamingMessage.Thinking ?? "") + _tokenBuffer;
                else
                    _streamingMessage.Content += _tokenBuffer;
                _tokenBuffer = "";
            }

            // Fallback: if OllamaSharp routed the entire response through OnThink
            // (e.g. Gemma3 puts everything in the thinking field), re-parse the
            // combined text so RouteStreamingToken can separate <think> from content.
            if (string.IsNullOrWhiteSpace(_streamingMessage.Content)
                && !string.IsNullOrEmpty(_streamingMessage.Thinking))
            {
                var rawThinking = _streamingMessage.Thinking;
                _streamingMessage.Content = "";
                _streamingMessage.Thinking = null;
                _inThinkBlock = false;
                _tokenBuffer = "";
                RouteStreamingToken(rawThinking);

                // Flush the buffer after the final parse
                if (_tokenBuffer.Length > 0)
                {
                    if (_inThinkBlock)
                        _streamingMessage.Thinking = (_streamingMessage.Thinking ?? "") + _tokenBuffer;
                    else
                        _streamingMessage.Content += _tokenBuffer;
                    _tokenBuffer = "";
                }

                // If still no content after re-parse (model produced no <think> tags,
                // so everything was genuine thinking with no answer), use it as content
                if (string.IsNullOrWhiteSpace(_streamingMessage.Content))
                {
                    _streamingMessage.Content = rawThinking;
                    _streamingMessage.Thinking = null;
                }
            }

            var assistantMsg = await ChatRepo.AddMessageAsync(
                activeSession.Id,
                ChatRole.Assistant,
                _streamingMessage.Content,
                modelName: activeModel,
                thinking: _streamingMessage.Thinking);
            activeSession.Messages.Add(assistantMsg);
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrEmpty(_streamingMessage.Content))
            {
                var assistantMsg = await ChatRepo.AddMessageAsync(
                    activeSession.Id,
                    ChatRole.Assistant,
                    _streamingMessage.Content,
                    modelName: activeModel,
                    thinking: _streamingMessage.Thinking);
                activeSession.Messages.Add(assistantMsg);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Chat stream error — model: {Model}, session: {SessionId}", activeModel, activeSession.Id);
            _streamingMessage.Content = $"⚠ Error: {ex.Message}";
            StateHasChanged();
        }
        finally
        {
            _isStreaming = false;
            _streamingSessionId = null;
            _streamingSession = null;
            _streamingCts?.Dispose();
            _streamingCts = null;
            UpdateTokenEstimate();
            StateHasChanged();
        }
    }

    // Routes each streamed token into either the think block or visible content,
    // using a small lookahead buffer so partial tag boundaries are handled safely.
    private void RouteStreamingToken(string token)
    {
        _tokenBuffer += token;

        while (true)
        {
            if (!_inThinkBlock)
            {
                var startIdx = _tokenBuffer.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
                if (startIdx == -1)
                {
                    var hold = PartialTagHold(_tokenBuffer, "<think>");
                    _streamingMessage.Content += _tokenBuffer[..(_tokenBuffer.Length - hold)];
                    _tokenBuffer = _tokenBuffer[(_tokenBuffer.Length - hold)..];
                    break;
                }
                _streamingMessage.Content += _tokenBuffer[..startIdx];
                _tokenBuffer = _tokenBuffer[(startIdx + "<think>".Length)..];
                _inThinkBlock = true;
            }
            else
            {
                var endIdx = _tokenBuffer.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
                if (endIdx == -1)
                {
                    var hold = PartialTagHold(_tokenBuffer, "</think>");
                    _streamingMessage.Thinking = (_streamingMessage.Thinking ?? "") + _tokenBuffer[..(_tokenBuffer.Length - hold)];
                    _tokenBuffer = _tokenBuffer[(_tokenBuffer.Length - hold)..];
                    break;
                }
                _streamingMessage.Thinking = (_streamingMessage.Thinking ?? "") + _tokenBuffer[..endIdx];
                _tokenBuffer = _tokenBuffer[(endIdx + "</think>".Length)..];
                _inThinkBlock = false;
            }
        }
    }

    // Returns how many chars at the end of `buf` could be the start of `tag`.
    private static int PartialTagHold(string buf, string tag)
    {
        for (var len = Math.Min(tag.Length - 1, buf.Length); len > 0; len--)
        {
            if (tag.StartsWith(buf[^len..], StringComparison.OrdinalIgnoreCase))
                return len;
        }
        return 0;
    }

    private void UpdateTokenEstimate()
    {
        if (_currentSession is null)
        {
            _estimatedTokenCount = 0;
            return;
        }

        var allText = string.Join(" ", _currentSession.Messages.Select(m => m.Content));
        _estimatedTokenCount = OllamaService.EstimateTokenCount(allText);
    }

    public void Dispose()
    {
        _streamingCts?.Cancel();
        _streamingCts?.Dispose();
    }
}
