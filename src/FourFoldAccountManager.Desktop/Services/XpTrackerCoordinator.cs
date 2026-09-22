using System.Net.Http;
using System.Windows.Threading;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Services;

public sealed record XpTrackerState(
    Guid AccountId,
    string? Username,
    double? RatePerHour,
    long SessionGain,
    string? ActiveClass,
    long? XpUntilNextLevel,
    double? HoursUntilNextLevel,
    DateTimeOffset? LastUpdated,
    string Status,
    bool IsStale);

public sealed class XpTrackerCoordinator : IAsyncDisposable
{
    private readonly FourFoldRankingClient _client = new();
    private readonly XpTrackerStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly Func<CancellationToken, Task> _runPollingLoop;
    private readonly Dictionary<Guid, TrackedAccount> _active = [];
    private readonly Dictionary<Guid, XpStoredAccount> _stored = [];
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private bool _loaded;

    public XpTrackerCoordinator(LocalDataPaths paths) : this(paths, null) { }

    // Tests can replace the background loop while exercising the real account/reset lifecycle.
    internal XpTrackerCoordinator(LocalDataPaths paths, Func<CancellationToken, Task>? runPollingLoop)
    {
        _store = new XpTrackerStore(paths);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _runPollingLoop = runPollingLoop ?? RunLoopAsync;
    }

    public event EventHandler? Changed;

    public async Task<bool> VerifyPlayerAsync(int playerId, string username)
    {
        var profile = await _client.GetProfileAsync(playerId, CancellationToken.None);
        return string.Equals(profile.Username, username.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<XpTrackerState> GetStates() => _active.Values.Select(account => new XpTrackerState(
        account.Id, account.Username, account.Session.RatePerHour, account.Session.SessionGain,
        account.Session.ActiveClassName, account.Session.XpUntilNextLevel,
        account.Session.HoursUntilNextLevel,
        account.Session.LastSuccessfulAt, account.Status, account.Session.IsStale)).ToArray();

    internal XpTrackingSession? GetSessionForTesting(Guid accountId) =>
        _active.TryGetValue(accountId, out var account) ? account.Session : null;

    public void Start(Guid accountId, string? rankingUsername, int? playerId)
    {
        _dispatcher.VerifyAccess();
        if (accountId == Guid.Empty) throw new ArgumentException("An account ID is required.", nameof(accountId));
        rankingUsername = rankingUsername?.Trim();
        if (_active.TryGetValue(accountId, out var previous))
        {
            if (previous.Identity.MatchesConfiguration(rankingUsername, playerId))
                return;
            previous.Session.Stop();
        }

        _active[accountId] = new TrackedAccount(accountId, rankingUsername, playerId);
        NotifyChanged();
        if (_loopTask is null)
        {
            _loopCancellation = new CancellationTokenSource();
            _loopTask = _runPollingLoop(_loopCancellation.Token);
        }
    }

    public void Stop(Guid accountId)
    {
        _dispatcher.VerifyAccess();
        if (_active.Remove(accountId, out var account)) account.Session.Stop();
        NotifyChanged();
        if (_active.Count == 0)
        {
            _loopCancellation?.Cancel();
            _loopTask = null;
            _loopCancellation = null;
        }
    }

    public void ResetRate(Guid accountId)
    {
        _dispatcher.VerifyAccess();
        if (_active.TryGetValue(accountId, out var account))
        {
            account.Session.ResetRate();
            NotifyChanged();
        }
    }

    public void ResetAll(Guid accountId)
    {
        _dispatcher.VerifyAccess();
        if (_active.TryGetValue(accountId, out var account))
        {
            account.Session.ResetAll();
            NotifyChanged();
        }
    }

    public void RefreshActiveAccounts(IReadOnlyList<Guid> visibleAccountIds)
    {
        _dispatcher.VerifyAccess();
        foreach (var id in _active.Keys.Except(visibleAccountIds).ToArray()) Stop(id);
        NotifyChanged();
    }

    public async Task RemoveAccountDataAsync(Guid accountId)
    {
        Stop(accountId);
        _stored.Remove(accountId);
        await _store.RemoveAsync(accountId);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                await PollOnceAsync(cancellationToken);
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            foreach (var account in _active.Values)
            {
                account.Session.MarkFetchFailed(DateTimeOffset.UtcNow);
                account.Status = "Tracker unavailable; retry when a client is reopened";
            }
            NotifyChanged();
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            foreach (var (id, entry) in await _store.LoadAsync(cancellationToken)) _stored[id] = entry;
            _loaded = true;
        }

        IReadOnlyList<RankingEntry>? ranking = null;
        try
        {
            ranking = await _client.GetRankingAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Known player IDs can still be sampled when the ranking is unavailable.
        }

        using var gate = new SemaphoreSlim(2, 2);
        var tasks = _active.Values.ToArray().Select(async account =>
        {
            await gate.WaitAsync(cancellationToken);
            try { await PollAccountAsync(account, ranking, cancellationToken); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task PollAccountAsync(
        TrackedAccount account, IReadOnlyList<RankingEntry>? ranking, CancellationToken cancellationToken)
    {
        if (!_active.TryGetValue(account.Id, out var current) || !ReferenceEquals(current, account)) return;
        if (string.IsNullOrWhiteSpace(account.Username))
        {
            account.Status = "Add a ranking username";
            NotifyChanged();
            return;
        }

        try
        {
            var playerId = account.PlayerId;
            if (playerId is null && _stored.TryGetValue(account.Id, out var cached) &&
                string.Equals(cached.Snapshot.Username, account.Username, StringComparison.OrdinalIgnoreCase))
                playerId = cached.PlayerId;
            if (playerId is null)
            {
                if (ranking is null) throw new HttpRequestException("Ranking unavailable.");
                var resolution = RankingIdentityResolver.Resolve(account.Username, ranking);
                if (resolution.Status != IdentityResolutionStatus.Matched)
                {
                    account.Status = resolution.Status == IdentityResolutionStatus.Ambiguous
                        ? "Multiple player matches; link a profile" : "Not in top 200; link a profile";
                    NotifyChanged();
                    return;
                }
                playerId = resolution.PlayerId;
            }

            var profile = await _client.GetProfileAsync(playerId!.Value, cancellationToken);
            if (!string.Equals(profile.Username, account.Username, StringComparison.OrdinalIgnoreCase))
            {
                account.PlayerId = null;
                account.Status = "Player name mismatch; check profile link";
                account.Session.MarkFetchFailed(DateTimeOffset.UtcNow);
                NotifyChanged();
                return;
            }

            if (!_active.TryGetValue(account.Id, out current) || !ReferenceEquals(current, account)) return;
            account.PlayerId = playerId;
            var sampledAt = DateTimeOffset.UtcNow;
            account.Session.ApplySnapshot(profile, sampledAt);
            account.Status = account.Session.HasUncertainInterval ? "Partial interval; sample missed or class reset" :
                account.Session.RatePerHour is null ? "Collecting baseline" : "Tracking";
            _stored[account.Id] = new XpStoredAccount(account.Id, playerId.Value, sampledAt, profile,
                account.Session.Intervals.ToArray());
            await _store.SaveAsync(_stored.Values.ToArray(), sampledAt, cancellationToken);
            NotifyChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            account.Session.MarkFetchFailed(DateTimeOffset.UtcNow);
            account.Status = "Stale; could not refresh player data";
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        if (_dispatcher.CheckAccess()) Changed?.Invoke(this, EventArgs.Empty);
        else _ = _dispatcher.InvokeAsync(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    public async ValueTask DisposeAsync()
    {
        _loopCancellation?.Cancel();
        if (_loopTask is { } task) await task;
        _loopCancellation?.Dispose();
        _client.Dispose();
    }

    private sealed class TrackedAccount(Guid id, string? username, int? playerId)
    {
        public Guid Id { get; } = id;
        public TrackedPlayerIdentity Identity { get; } = new(username, playerId);
        public string? Username => Identity.Username;
        public int? PlayerId { get => Identity.ResolvedPlayerId; set => Identity.ResolvedPlayerId = value; }
        public XpTrackingSession Session { get; } = new();
        public string Status { get; set; } = "Collecting baseline";
    }
}
