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
using Weavenest.Services.Models;

namespace Weavenest.Components.Pages;

public partial class Chat : IDisposable
{
    [Parameter] public Guid? SessionId { get; set; }

    private string InputAdornmentIcon => _isProcessing
        ? Icons.Material.Filled.Stop
        : Icons.Material.Filled.Send;

    private Color InputAdornmentColor => _isProcessing
        ? Color.Error
        : Color.Primary;

    private string InputAdornmentLabel => _isProcessing ? "Stop" : "Send";

    private async Task HandleAdornmentClick()
    {
        if (_isProcessing)
            _processingCts?.Cancel();
        else
            await SendMessage();
    }

    private string SessionActivityTooltip
    {
        get
        {
            var parts = new List<string>();
            if (_displayMessages.Count > 0)
                parts.Add($"{_displayMessages.Count(m => m.Role == "tool_call")} tool call(s) this session");
            if (_sessionWhitelistedDomains.Count > 0)
                parts.Add($"{_sessionWhitelistedDomains.Count} approved site(s)");
            return parts.Count > 0
                ? string.Join(" · ", parts)
                : "Session activity (tool calls & approved sites)";
        }
    }

    private int SessionActivityBadge =>
        _displayMessages.Count(m => m.Role == "tool_call") + _sessionWhitelistedDomains.Count;

    private string UserPromptTooltip => !string.IsNullOrWhiteSpace(Settings.UserPrompt)
        ? $"User memory active: \"{Settings.UserPrompt[..Math.Min(40, Settings.UserPrompt.Length)]}...\""
        : "Configure user memory";

    /// <summary>Whether the current view is the session being processed (guards UI indicators).</summary>
    private bool IsViewingProcessingSession =>
        _isProcessing && SessionId.HasValue && SessionId.Value == _processingSessionId;

    private ChatSession? _currentSession;
    private List<string> _availableModels = [];
    private string _selectedModel = "";
    private string _userInput = "";
    private bool _isProcessing;
    private Guid? _processingSessionId;
    private ChatSession? _processingSession;
    private int _estimatedTokenCount;
    private int _contextLength = 2048;
    private CancellationTokenSource? _processingCts;
    private ElementReference _messagesContainer;
    private string? _ollamaError;
    private bool _webSearchEnabled = true;
    private bool _showThinking = true;
    private bool _modelSupportsTools = true;
    private bool _modelSupportsThinking = true;

    // Agentic mode state
    private List<AgenticDisplayMessage> _displayMessages = [];
    private HashSet<string> _sessionWhitelistedDomains = new(StringComparer.OrdinalIgnoreCase);

    // Deep research state — per-chat toggle, not a global service
    private bool _deepResearchEnabled;
    private bool _deepResearchComplete;
    private bool _deepResearchWasActive; // captures the flag at send-time for the processing session
    private bool _trustAllDomainsForResearchRun;
    private ElementReference _researchFeedContainer;

    // Streaming mode state
    private bool _isStreaming;
    private ChatMessage _streamingMessage = new() { Role = ChatRole.Assistant, Content = "" };
    private bool _inThinkBlock;
    private string _tokenBuffer = "";

    private void ToggleWebSearch() => _webSearchEnabled = !_webSearchEnabled;
    private void ToggleShowThinking() => _showThinking = !_showThinking;
    private void ToggleDeepResearch() => _deepResearchEnabled = !_deepResearchEnabled;

    protected override async Task OnInitializedAsync()
    {
        await LoadModels();
        await LoadUserPromptIfNeeded();
    }

    private async Task LoadUserPromptIfNeeded()
    {
        if (Settings.UserPromptLoaded) return;
        var userId = await UserIdentity.GetCurrentUserIdAsync();
        if (userId.HasValue)
        {
            var user = await UserRepo.GetByIdAsync(userId.Value);
            Settings.UserPrompt = user?.UserPrompt;
            Settings.UserPromptLoaded = true;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (SessionId.HasValue)
        {
            // If we're viewing the session that's currently being processed, keep the live state
            if (_isProcessing && SessionId.Value == _processingSessionId)
            {
                _currentSession = _processingSession;
                return;
            }

            _currentSession = await ChatRepo.GetSessionByIdAsync(SessionId.Value);
            if (_currentSession is not null)
            {
                _selectedModel = _currentSession.ModelName ?? _selectedModel;
                UpdateTokenEstimate();
                await UpdateContextInfo();

                // Load persisted whitelist for this session
                await LoadWhitelistFromDb(SessionId.Value);
            }

            // When switching to a non-processing session, reset per-view state
            // (the processing task keeps running; its state is tied to _processingSessionId)
            if (!_isProcessing || SessionId.Value != _processingSessionId)
            {
                _displayMessages = [];
                _deepResearchComplete = false;
                _deepResearchEnabled = false;
                _trustAllDomainsForResearchRun = false;
            }
        }
        else
        {
            _currentSession = null;
            _estimatedTokenCount = 0;
            _sessionWhitelistedDomains = new(StringComparer.OrdinalIgnoreCase);
            _trustAllDomainsForResearchRun = false;
            _displayMessages = [];
            _deepResearchComplete = false;
            _deepResearchEnabled = false;
        }
    }

    private async Task LoadWhitelistFromDb(Guid sessionId)
    {
        try
        {
            var domains = await ChatRepo.GetWhitelistedDomainsAsync(sessionId);
            _sessionWhitelistedDomains = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load whitelisted domains for session {SessionId}", sessionId);
            _sessionWhitelistedDomains = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task LoadModels()
    {
        try
        {
            var models = await OllamaService.GetModelsAsync();
            _availableModels = models.ToList();
            if (_availableModels.Count > 0 && string.IsNullOrEmpty(_selectedModel))
            {
                _selectedModel = _availableModels[0];
            }
            await UpdateContextInfo();
            _ollamaError = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to connect to Ollama");
            _ollamaError = "Could not connect to Ollama. Make sure Ollama is running locally and the URL in appsettings.json is correct.";
        }
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

            var caps = await OllamaService.GetModelCapabilitiesAsync(_selectedModel);
            _modelSupportsTools = caps.SupportsTools;
            _modelSupportsThinking = caps.SupportsThinking;
        }
    }

    private async Task OpenUserMemoryDialog()
    {
        var parameters = new DialogParameters
        {
            ["UserPrompt"] = Settings.UserPrompt
        };

        var options = new DialogOptions
        {
            CloseButton = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true
        };

        var dialog = await DialogService.ShowAsync<UserPromptDialog>("User Memory", parameters, options);
        var result = await dialog.Result;

        if (!result!.Canceled)
        {
            var newPrompt = result.Data as string;
            Settings.UserPrompt = newPrompt;

            var userId = await UserIdentity.GetCurrentUserIdAsync();
            if (userId.HasValue)
            {
                await UserRepo.UpdateUserPromptAsync(userId.Value, newPrompt);
            }
        }
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey && !_isProcessing)
        {
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(_userInput) || _isProcessing)
            return;

        if (string.IsNullOrEmpty(_selectedModel))
            return;

        var userText = _userInput.Trim();
        _userInput = "";
        _ollamaError = null;

        if (_currentSession is null)
        {
            var userId = await UserIdentity.GetCurrentUserIdAsync();
            if (userId is null)
                return;

            var title = userText.Length > 50
                ? userText[..50] + "..."
                : userText;
            _currentSession = await ChatRepo.CreateSessionAsync(userId.Value, title, _selectedModel);

            // New session — clear any whitelist carried over from a previously-viewed session
            _sessionWhitelistedDomains = new(StringComparer.OrdinalIgnoreCase);
            _trustAllDomainsForResearchRun = false;

            _isProcessing = true;
            _processingSessionId = _currentSession.Id;
            _processingSession = _currentSession;

            await StateNotifier.NotifySessionsChanged();
            Navigation.NavigateTo($"/chat/{_currentSession.Id}", replace: true);
        }

        var activeSession = _currentSession;
        var activeModel = _selectedModel;

        var userMsg = await ChatRepo.AddMessageAsync(activeSession.Id, ChatRole.User, userText);
        activeSession.Messages.Add(userMsg);

        if (!_isProcessing)
        {
            _isProcessing = true;
            _processingSessionId = activeSession.Id;
            _processingSession = activeSession;
        }

        _processingCts = new CancellationTokenSource();

        // Capture the deep research flag at send-time so it survives session switches
        _deepResearchWasActive = _deepResearchEnabled;

        if (_webSearchEnabled || _deepResearchEnabled)
            await SendMessageAgentic(activeSession, activeModel, userText);
        else
            await SendMessageStreaming(activeSession, activeModel, userText);
    }

    private async Task SendMessageStreaming(ChatSession activeSession, string activeModel, string userText)
    {
        _isStreaming = true;
        _inThinkBlock = false;
        _tokenBuffer = "";
        _streamingMessage = new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "",
            ModelName = activeModel
        };
        StateHasChanged();

        try
        {
            var effectivePrompt = BuildEffectivePrompt();

            var history = activeSession.Messages
                .Where(m => m.Role != ChatRole.Assistant || m.Content.Length > 0)
                .ToList();

            // Remove the last message (the user message we just added) — ChatStreamAsync sends it separately
            if (history.Count > 0 && history[^1].Role == ChatRole.User)
                history.RemoveAt(history.Count - 1);

            await foreach (var token in OllamaService.ChatStreamAsync(
                history, userText, activeModel,
                systemPrompt: effectivePrompt,
                onThinkToken: t =>
                {
                    _streamingMessage.Thinking = (_streamingMessage.Thinking ?? "") + t;
                    InvokeAsync(StateHasChanged);
                },
                cancellationToken: _processingCts!.Token))
            {
                RouteStreamingToken(token);
                StateHasChanged();

                try
                {
                    await JSRuntime.InvokeVoidAsync("chatInterop.scrollToBottom", _messagesContainer);
                }
                catch { }
            }

            FlushStreamingBuffer();
            HandleThinkingFallback();

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
            Logger.LogInformation("Chat streaming cancelled — session: {SessionId}", activeSession.Id);
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
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Ollama unreachable — model: {Model}, session: {SessionId}", activeModel, activeSession.Id);
            _ollamaError = $"Could not connect to Ollama: {ex.Message}. Make sure Ollama is running locally.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Chat stream error — model: {Model}, session: {SessionId}", activeModel, activeSession.Id);
            _ollamaError = $"Error: {ex.Message}";
        }
        finally
        {
            _isStreaming = false;
            _isProcessing = false;
            _processingSessionId = null;
            _processingSession = null;
            _processingCts?.Dispose();
            _processingCts = null;
            UpdateTokenEstimate();
            StateHasChanged();
        }
    }

    private async Task SendMessageAgentic(ChatSession activeSession, string activeModel, string userText)
    {
        _displayMessages = [];
        _deepResearchComplete = false;
        _trustAllDomainsForResearchRun = false;

        // Capture at send-time so it doesn't change if the user switches sessions
        var deepResearchActive = _deepResearchWasActive;
        StateHasChanged();

        try
        {
            var effectivePrompt = BuildEffectivePrompt();

            var history = activeSession.Messages
                .Where(m => m.Role != ChatRole.System)
                .Select(m => new OllamaChatMessage
                {
                    Role = m.Role == ChatRole.User ? "user" : "assistant",
                    Content = m.Content
                })
                .ToList();

            // Remove the last message (the user message we just added) — the agentic service appends it
            if (history.Count > 0 && history[^1].Role == "user")
                history.RemoveAt(history.Count - 1);

            var request = new AgenticChatRequest
            {
                ModelName = activeModel,
                SystemPrompt = effectivePrompt,
                History = history,
                UserMessage = userText,
                WebSearchEnabled = _webSearchEnabled,
                DeepResearchEnabled = deepResearchActive
            };

            var result = await AgenticChat.RunAsync(
                request,
                urlApprovalCallback: async url =>
                {
                    var approved = false;
                    await InvokeAsync(async () => approved = await UrlApprovalCallback(url, activeSession.Id, deepResearchActive));
                    return approved;
                },
                onDisplayMessage: dm => InvokeAsync(async () =>
                {
                    _displayMessages.Add(dm);
                    StateHasChanged();
                    try
                    {
                        await JSRuntime.InvokeVoidAsync("chatInterop.scrollToBottom", _messagesContainer);
                        if (deepResearchActive)
                            await JSRuntime.InvokeVoidAsync("chatInterop.scrollToBottom", _researchFeedContainer);
                    }
                    catch { }
                }),
                ct: _processingCts!.Token);

            var assistantMsg = await ChatRepo.AddMessageAsync(
                activeSession.Id,
                ChatRole.Assistant,
                result.AssistantContent,
                modelName: activeModel,
                thinking: result.Thinking);
            activeSession.Messages.Add(assistantMsg);
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("Chat processing cancelled — session: {SessionId}", activeSession.Id);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Ollama unreachable — model: {Model}, session: {SessionId}", activeModel, activeSession.Id);
            _ollamaError = $"Could not connect to Ollama: {ex.Message}. Make sure Ollama is running locally.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Chat error — model: {Model}, session: {SessionId}", activeModel, activeSession.Id);
            _ollamaError = $"Error: {ex.Message}";
        }
        finally
        {
            _isProcessing = false;
            _processingSessionId = null;
            _processingSession = null;
            _processingCts?.Dispose();
            _processingCts = null;
            if (deepResearchActive)
                _deepResearchComplete = true;
            UpdateTokenEstimate();
            StateHasChanged();
        }
    }

    private string BuildEffectivePrompt()
    {
        var effectivePrompt = Settings.SystemPrompt ?? "";
        if (!string.IsNullOrWhiteSpace(Settings.UserPrompt))
        {
            effectivePrompt = $"{effectivePrompt}\n\n## Persistent User Context\nThe following is background context about this user, provided by the system. It was not said by the user in this conversation — treat it as reference information to inform your responses:\n\n{Settings.UserPrompt}";
        }
        return effectivePrompt.Replace("{{currentDateTime}}", DateTime.Now.ToString());
    }

    private IEnumerable<ChatMessage> GetSessionMessagesForRender()
    {
        if (_currentSession is null) return [];
        var msgs = _currentSession.Messages;

        if (_displayMessages.Count > 0 && msgs.Count > 0 && msgs[^1].Role == ChatRole.Assistant)
            return msgs.Take(msgs.Count - 1);

        return msgs;
    }

    private ChatMessage? GetDeferredAssistantMessage()
    {
        if (_displayMessages.Count == 0 || _currentSession is null) return null;
        var msgs = _currentSession.Messages;
        if (msgs.Count > 0 && msgs[^1].Role == ChatRole.Assistant)
            return msgs[^1];
        return null;
    }

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

    private static int PartialTagHold(string buf, string tag)
    {
        for (var len = Math.Min(tag.Length - 1, buf.Length); len > 0; len--)
        {
            if (tag.StartsWith(buf[^len..], StringComparison.OrdinalIgnoreCase))
                return len;
        }
        return 0;
    }

    private void FlushStreamingBuffer()
    {
        if (_tokenBuffer.Length > 0)
        {
            if (_inThinkBlock)
                _streamingMessage.Thinking = (_streamingMessage.Thinking ?? "") + _tokenBuffer;
            else
                _streamingMessage.Content += _tokenBuffer;
            _tokenBuffer = "";
        }
    }

    private void HandleThinkingFallback()
    {
        if (string.IsNullOrWhiteSpace(_streamingMessage.Content)
            && !string.IsNullOrEmpty(_streamingMessage.Thinking))
        {
            var rawThinking = _streamingMessage.Thinking;
            _streamingMessage.Content = "";
            _streamingMessage.Thinking = null;
            _inThinkBlock = false;
            _tokenBuffer = "";
            RouteStreamingToken(rawThinking);
            FlushStreamingBuffer();

            if (string.IsNullOrWhiteSpace(_streamingMessage.Content))
            {
                _streamingMessage.Content = rawThinking;
                _streamingMessage.Thinking = null;
            }
        }
    }

    private async Task OpenSessionInfoDialog()
    {
        if (_currentSession is null) return;

        var parameters = new DialogParameters
        {
            ["ToolCalls"] = _displayMessages.ToList(),
            ["WhitelistedDomains"] = _sessionWhitelistedDomains,
            ["SessionId"] = _currentSession.Id
        };

        var options = new DialogOptions
        {
            CloseButton = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true
        };

        var dialog = await DialogService.ShowAsync<SessionInfoDialog>("Session Activity", parameters, options);
        var result = await dialog.Result;

        // Reload whitelist from DB after the dialog closes (user may have removed domains)
        await LoadWhitelistFromDb(_currentSession.Id);
    }

    private async Task<bool> UrlApprovalCallback(string url, Guid sessionId, bool isDeepResearch)
    {
        try
        {
            if (_trustAllDomainsForResearchRun)
                return true;

            var uri = new Uri(url);
            var domain = uri.Host;

            if (_sessionWhitelistedDomains.Contains(domain))
                return true;

            var parameters = new DialogParameters
            {
                ["Url"] = url,
                ["IsDeepResearch"] = isDeepResearch
            };

            var options = new DialogOptions
            {
                CloseButton = false,
                MaxWidth = MaxWidth.Small,
                FullWidth = true
            };

            var dialog = await DialogService.ShowAsync<WebFetchApprovalDialog>(
                "Web Page Access", parameters, options);
            var result = await dialog.Result;

            if (result?.Canceled != false)
                return false;

            if (result.Data is string s && s == "trust_all")
            {
                _trustAllDomainsForResearchRun = true;
                _sessionWhitelistedDomains.Add(domain);
                await PersistWhitelistedDomain(sessionId, domain);
                return true;
            }

            var allowed = (bool)result.Data!;
            if (allowed)
            {
                _sessionWhitelistedDomains.Add(domain);
                await PersistWhitelistedDomain(sessionId, domain);
            }

            return allowed;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "URL approval callback failed for: {Url}", url);
            return false;
        }
    }

    private async Task PersistWhitelistedDomain(Guid sessionId, string domain)
    {
        try
        {
            await ChatRepo.AddWhitelistedDomainAsync(sessionId, domain);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to persist whitelisted domain {Domain} for session {SessionId}", domain, sessionId);
        }
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

    private static string GetResearchEventIcon(string eventType) => eventType switch
    {
        "research_plan" => Icons.Material.Filled.Article,
        "subquery_start" => Icons.Material.Filled.Search,
        "search" => Icons.Material.Filled.TravelExplore,
        "fetch_approved" => Icons.Material.Filled.Download,
        "fetch_denied" => Icons.Material.Filled.Block,
        "fetch_failed" => Icons.Material.Filled.Warning,
        "gap_search" => Icons.Material.Filled.FindInPage,
        "finalizing" => Icons.Material.Filled.Summarize,
        _ => Icons.Material.Filled.Info
    };

    public void Dispose()
    {
        _processingCts?.Cancel();
        _processingCts?.Dispose();
    }
}
