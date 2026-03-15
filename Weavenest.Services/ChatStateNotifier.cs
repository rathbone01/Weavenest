namespace Weavenest.Services;

public class ChatStateNotifier
{
    public event Func<Task>? OnSessionsChanged;

    public async Task NotifySessionsChanged()
    {
        if (OnSessionsChanged is not null)
            await OnSessionsChanged.Invoke();
    }
}
