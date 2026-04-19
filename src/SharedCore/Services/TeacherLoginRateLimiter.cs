using System.Collections.Concurrent;

namespace SharedCore.Services;

public sealed class TeacherLoginRateLimiter
{
    private readonly ConcurrentDictionary<string, AttemptWindow> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _window = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _lockout = TimeSpan.FromMinutes(10);
    private readonly int _maxAttempts = 5;

    public bool IsBlocked(string username, string remoteAddress, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var key = BuildKey(username, remoteAddress);
        if (!_attempts.TryGetValue(key, out var attempt))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (attempt.BlockedUntilUtc.HasValue && attempt.BlockedUntilUtc.Value > now)
        {
            retryAfter = attempt.BlockedUntilUtc.Value - now;
            return true;
        }

        if (now - attempt.FirstAttemptUtc > _window)
        {
            _attempts.TryRemove(key, out _);
        }

        return false;
    }

    public void RegisterFailure(string username, string remoteAddress)
    {
        var key = BuildKey(username, remoteAddress);
        var now = DateTime.UtcNow;
        _attempts.AddOrUpdate(
            key,
            _ => new AttemptWindow(now, 1, null),
            (_, current) =>
            {
                var firstAttemptUtc = now - current.FirstAttemptUtc > _window ? now : current.FirstAttemptUtc;
                var failures = firstAttemptUtc == now ? 1 : current.Failures + 1;
                var blockedUntilUtc = failures >= _maxAttempts ? now.Add(_lockout) : current.BlockedUntilUtc;
                return new AttemptWindow(firstAttemptUtc, failures, blockedUntilUtc);
            });
    }

    public void RegisterSuccess(string username, string remoteAddress)
    {
        _attempts.TryRemove(BuildKey(username, remoteAddress), out _);
    }

    private static string BuildKey(string username, string remoteAddress) =>
        $"{username.Trim().ToLowerInvariant()}|{remoteAddress.Trim()}";

    private sealed record AttemptWindow(
        DateTime FirstAttemptUtc,
        int Failures,
        DateTime? BlockedUntilUtc);
}
