namespace FourFoldAccountManager.Leaderboard.Service.Collection;

// Process-wide sampler state: wakes the worker when a profile starts participating and
// keeps a failed player's next request at least one sample interval away.
public sealed class LeaderboardSamplingSchedule
{
    private readonly object _gate = new();
    private readonly Dictionary<int, DateTimeOffset> _retryAfter = [];
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void RequestSample()
    {
        lock (_gate) _wake.TrySetResult();
    }

    public async Task WaitForWorkAsync(TimeSpan timeout, TimeProvider clock, CancellationToken ct)
    {
        Task wake;
        lock (_gate) wake = _wake.Task;
        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, clock, delayCancellation.Token);
        await Task.WhenAny(wake, delay);
        delayCancellation.Cancel();
        lock (_gate)
        {
            if (_wake.Task.IsCompleted)
                _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    internal bool CanSample(int playerId, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_retryAfter.TryGetValue(playerId, out var retryAfter)) return true;
            if (retryAfter > nowUtc) return false;
            _retryAfter.Remove(playerId);
            return true;
        }
    }

    internal void Defer(int playerId, DateTimeOffset retryAfterUtc)
    {
        lock (_gate) _retryAfter[playerId] = retryAfterUtc;
    }

    internal void Succeeded(int playerId)
    {
        lock (_gate) _retryAfter.Remove(playerId);
    }
}
