using System.Collections.Concurrent;

namespace Weavenest.Services;

public class LoginRateLimiter
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, LoginAttemptTracker> _attempts = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLockedOut(string username)
    {
        if (!_attempts.TryGetValue(username, out var tracker))
            return false;

        lock (tracker)
        {
            if (tracker.LockedUntil.HasValue)
            {
                if (DateTime.UtcNow < tracker.LockedUntil.Value)
                    return true;

                // Lockout expired — reset
                tracker.LockedUntil = null;
                tracker.FailedAttempts.Clear();
            }
            return false;
        }
    }

    public void RecordFailedAttempt(string username)
    {
        var tracker = _attempts.GetOrAdd(username, _ => new LoginAttemptTracker());
        lock (tracker)
        {
            var now = DateTime.UtcNow;
            tracker.FailedAttempts.Enqueue(now);

            // Remove attempts outside the window
            while (tracker.FailedAttempts.Count > 0 && now - tracker.FailedAttempts.Peek() > Window)
                tracker.FailedAttempts.Dequeue();

            if (tracker.FailedAttempts.Count >= MaxAttempts)
                tracker.LockedUntil = now + LockoutDuration;
        }
    }

    public void RecordSuccessfulLogin(string username)
    {
        _attempts.TryRemove(username, out _);
    }

    private class LoginAttemptTracker
    {
        public Queue<DateTime> FailedAttempts { get; } = new();
        public DateTime? LockedUntil { get; set; }
    }
}
