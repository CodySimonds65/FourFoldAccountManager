using FourFoldAccountManager.Core.Leaderboard;
using System.Net.Http;
using System.IO;
using System.Text.Json;

namespace FourFoldAccountManager.Desktop.Services;

public sealed record LeaderboardStatus(bool IsOnline, bool ShowingCachedData, DateTimeOffset? LastUpdatedAtUtc);

public sealed class LeaderboardCoordinator : IAsyncDisposable
{
    private readonly LeaderboardClientStateStore _store;
    private readonly LeaderboardApiClient? _client;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<(LeaderboardPeriod Period, int Page, int PageSize)> _pageRetries = [];
    private LeaderboardClientState? _state;
    private bool _disposed;

    public LeaderboardCoordinator(LeaderboardClientStateStore store, LeaderboardApiClient? client)
    {
        _store = store;
        _client = client;
    }

    public event EventHandler<LeaderboardStatus>? StatusChanged;
    public bool IsConfigured => _client is not null;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _stateGate.WaitAsync(ct);
        try { _state ??= await _store.LoadAsync(ct); }
        finally { _stateGate.Release(); }
    }

    public async Task<LeaderboardPage?> GetCachedPageAsync(LeaderboardPeriod period, int page, int pageSize,
        CancellationToken ct)
    {
        // Reading the local snapshot must not start a request or a retry loop.
        var state = _state ?? await _store.LoadAsync(ct);
        return state.CachedPages.FirstOrDefault(candidate =>
            candidate.Period == period && candidate.Page == page && candidate.PageSize == pageSize);
    }

    public async Task<LeaderboardPage> GetPageAsync(LeaderboardPeriod period, int page, int pageSize,
        CancellationToken ct)
    {
        await InitializeAsync(ct);
        var cached = _state!.CachedPages.FirstOrDefault(candidate =>
            candidate.Period == period && candidate.Page == page && candidate.PageSize == pageSize);
        if (cached is not null)
        {
            _ = RefreshPageAsync(period, page, pageSize, _lifetime.Token);
            return cached;
        }
        var fetched = await RefreshPageAsync(period, page, pageSize, ct);
        if (fetched is not null) return fetched;
        var (start, end) = LeaderboardPeriodWindow.GetCurrent(period, DateTimeOffset.UtcNow);
        return new LeaderboardPage(period, start, end, page, pageSize, 0, DateTimeOffset.UtcNow, []);
    }

    public async Task<LeaderboardPage?> RefreshPageAsync(LeaderboardPeriod period, int page, int pageSize,
        CancellationToken ct, bool scheduleRetry = true)
    {
        if (_client is null) return null;
        try
        {
            var fresh = await _client.GetPageAsync(period, page, pageSize, ct);
            await _stateGate.WaitAsync(ct);
            try
            {
                _state ??= await _store.LoadAsync(ct);
                var pages = _state.CachedPages.Where(candidate =>
                        candidate.Period != period || candidate.Page != page || candidate.PageSize != pageSize)
                    .Append(fresh).ToArray();
                _state = _state with { CachedPages = pages };
                await _store.SaveAsync(_state, ct);
            }
            finally { _stateGate.Release(); }
            StatusChanged?.Invoke(this, new LeaderboardStatus(true, false, fresh.GeneratedAtUtc));
            return fresh;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or OperationCanceledException or IOException)
        {
            StatusChanged?.Invoke(this, new LeaderboardStatus(false, true,
                _state?.CachedPages.FirstOrDefault(candidate => candidate.Period == period)?.GeneratedAtUtc));
            if (scheduleRetry && !_lifetime.IsCancellationRequested)
                SchedulePageRetry(period, page, pageSize);
            return null;
        }
    }

    private void SchedulePageRetry(LeaderboardPeriod period, int page, int pageSize)
    {
        var key = (period, page, pageSize);
        lock (_pageRetries)
        {
            if (!_pageRetries.Add(key)) return;
        }
        _ = RetryPageAsync(key, _lifetime.Token);
    }

    private async Task RetryPageAsync((LeaderboardPeriod Period, int Page, int PageSize) key,
        CancellationToken ct)
    {
        try
        {
            foreach (var delaySeconds in new[] { 15, 30, 60, 120 })
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                if (await RefreshPageAsync(key.Period, key.Page, key.PageSize, ct,
                        scheduleRetry: false) is not null) return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            lock (_pageRetries) _pageRetries.Remove(key);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _lifetime.Cancel();
        _client?.Dispose();
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }
}
