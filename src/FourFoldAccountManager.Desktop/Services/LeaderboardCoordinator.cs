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
    private readonly Func<bool, CancellationToken, Task> _persistSharing;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<(LeaderboardPeriod Period, int Page, int PageSize)> _pageRetries = [];
    private LeaderboardClientState? _state;
    private IReadOnlyList<LeaderboardProfile> _linkedProfiles = [];
    private IReadOnlyList<int> _activePlayerIds = [];
    private Task? _renewalTask;
    private Task? _retryTask;
    private bool _sharingEnabled;
    private bool _hasSynced;
    private bool _disposed;

    public LeaderboardCoordinator(LeaderboardClientStateStore store, LeaderboardApiClient? client,
        bool sharingEnabled, Func<bool, CancellationToken, Task> persistSharing)
    {
        _store = store;
        _client = client;
        _sharingEnabled = sharingEnabled;
        _persistSharing = persistSharing;
    }

    public event EventHandler<LeaderboardStatus>? StatusChanged;
    public Guid InstallationId => _state?.InstallationId ?? Guid.Empty;
    public bool IsConfigured => _client is not null;
    public bool SharingEnabled => _sharingEnabled;
    public bool HasPendingParticipation => _state?.PendingParticipation is not null;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _stateGate.WaitAsync(ct);
        try
        {
            if (_state is not null) return;
            _state = await _store.LoadAsync(ct);
            if (_state.PendingParticipation is { } pending)
            {
                var safePending = !_sharingEnabled
                    ? DisabledHeartbeat(_state.InstallationId)
                    : pending with { ActivePlayerIds = [] };
                _state = _state with { PendingParticipation = safePending };
            }
            await _store.SaveAsync(_state, ct);
        }
        finally { _stateGate.Release(); }
        if (HasPendingParticipation) StartRetryLoop();
    }

    public async Task SetSharingEnabledAsync(bool enabled, CancellationToken ct)
    {
        // The opt-out is durable before the first network request is attempted.
        await _persistSharing(enabled, ct);
        _sharingEnabled = enabled;
        await QueueParticipationAsync(ct);
    }

    public async Task SyncParticipationAsync(IReadOnlyList<LeaderboardProfile> linkedProfiles,
        IReadOnlyCollection<int> activePlayerIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(linkedProfiles);
        ArgumentNullException.ThrowIfNull(activePlayerIds);
        var nextLinkedProfiles = linkedProfiles.Where(profile => profile.PlayerId > 0 &&
                !string.IsNullOrWhiteSpace(profile.Username))
            .GroupBy(profile => profile.PlayerId)
            .Select(group => group.First()).Take(1000).ToArray();
        var linkedIds = nextLinkedProfiles.Select(profile => profile.PlayerId).ToHashSet();
        var nextActivePlayerIds = activePlayerIds.Where(linkedIds.Contains).Distinct().Take(5).ToArray();
        var changed = !_hasSynced || !_linkedProfiles.SequenceEqual(nextLinkedProfiles) ||
                      !_activePlayerIds.SequenceEqual(nextActivePlayerIds);
        _linkedProfiles = nextLinkedProfiles;
        _activePlayerIds = nextActivePlayerIds;
        _hasSynced = true;
        if (changed && (_sharingEnabled || HasPendingParticipation))
            await QueueParticipationAsync(ct);
        if (_sharingEnabled && _activePlayerIds.Count > 0 && _renewalTask is null)
            _renewalTask = RunRenewalLoopAsync(_lifetime.Token);
    }

    private async Task QueueParticipationAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await _stateGate.WaitAsync(ct);
        try
        {
            var heartbeat = _sharingEnabled
                ? new ParticipationHeartbeat(_state!.InstallationId, true, _linkedProfiles, _activePlayerIds)
                : DisabledHeartbeat(_state!.InstallationId);
            _state = _state with { PendingParticipation = heartbeat };
            await _store.SaveAsync(_state, ct);
        }
        finally { _stateGate.Release(); }
        _ = FlushPendingAsync(_lifetime.Token);
        StartRetryLoop();
    }

    private void StartRetryLoop()
    {
        if (_client is null || _retryTask is { IsCompleted: false } || _disposed) return;
        _retryTask = RunRetryLoopAsync(_lifetime.Token);
    }

    private async Task RunRetryLoopAsync(CancellationToken ct)
    {
        var delaySeconds = 15;
        try
        {
            while (HasPendingParticipation && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                await FlushPendingAsync(ct);
                delaySeconds = Math.Min(delaySeconds * 2, 120);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static ParticipationHeartbeat DisabledHeartbeat(Guid id) => new(id, false, [], []);

    public async Task FlushPendingAsync(CancellationToken ct = default)
    {
        if (_client is null || _disposed) return;
        var acquired = false;
        try
        {
            await _requestGate.WaitAsync(ct);
            acquired = true;
            var pending = _state?.PendingParticipation;
            if (pending is null) return;
            try
            {
                await _client.SendHeartbeatAsync(pending, ct);
                await _stateGate.WaitAsync(ct);
                try
                {
                    if (ReferenceEquals(_state?.PendingParticipation, pending))
                    {
                        _state = _state with { PendingParticipation = null };
                        await _store.SaveAsync(_state, ct);
                    }
                }
                finally { _stateGate.Release(); }
                StatusChanged?.Invoke(this, new LeaderboardStatus(true, false, DateTimeOffset.UtcNow));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException or IOException)
            {
                StatusChanged?.Invoke(this, new LeaderboardStatus(false, true, null));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { if (acquired) _requestGate.Release(); }
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
        await EnsureInitializedAsync(ct);
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
        => await RefreshPageCoreAsync(period, page, pageSize, ct, scheduleRetry);

    private async Task<LeaderboardPage?> RefreshPageCoreAsync(LeaderboardPeriod period, int page, int pageSize,
        CancellationToken ct, bool scheduleRetry)
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
                if (await RefreshPageCoreAsync(key.Period, key.Page, key.PageSize, ct,
                        scheduleRetry: false) is not null) return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            lock (_pageRetries) _pageRetries.Remove(key);
        }
    }

    private async Task RunRenewalLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_sharingEnabled && _activePlayerIds.Count > 0)
                    await QueueParticipationAsync(ct);
                else if (HasPendingParticipation)
                    await FlushPendingAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private Task EnsureInitializedAsync(CancellationToken ct) => _state is null ? InitializeAsync(ct) : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (_renewalTask is not null) await _renewalTask;
        if (_retryTask is not null) await _retryTask;
        _client?.Dispose();
        _lifetime.Dispose();
    }
}
