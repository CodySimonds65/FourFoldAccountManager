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
    private readonly PlayerProfileService _profileService;
    private readonly IDisposable? _ownedTransport;
    private readonly XpTrackerStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly Func<CancellationToken, Task> _runPollingLoop;
    private readonly Dictionary<Guid, TrackedAccount> _active = [];
    private readonly Dictionary<Guid, XpStoredAccount> _stored = [];
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private bool _loaded;

    public XpTrackerCoordinator(LocalDataPaths paths) : this(paths, null, null) { }

    // Tests can replace the background loop while exercising the real account/reset lifecycle.
    internal XpTrackerCoordinator(
        LocalDataPaths paths,
        Func<CancellationToken, Task>? runPollingLoop,
        PlayerProfileService? profileService = null)
    {
        _store = new XpTrackerStore(paths);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _runPollingLoop = runPollingLoop ?? RunLoopAsync;
        if (profileService is not null)
        {
            _profileService = profileService;
        }
        else
        {
            var client = new FourFoldRankingClient();
            _profileService = new PlayerProfileService(client);
            _ownedTransport = client;
        }
    }

    public event EventHandler? Changed;

    // Raised after each successful profile read, on the poll's thread. The live feed checks its XP against it.
    public event Action<Guid, PlayerProgressSnapshot, DateTimeOffset>? ProfileSampled;

    public PlayerProfileService ProfileService => _profileService;

    // Tests shorten the pause before the one retry of a failed profile read.
    internal TimeSpan ProfileRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public async Task<bool> VerifyPlayerAsync(int playerId, string username)
    {
        var result = await _profileService.ReadAsync(Guid.NewGuid(), username, playerId, CancellationToken.None);
        return result.IsSuccess;
    }

    public IReadOnlyList<XpTrackerState> GetStates() => _active.Values.Select(account => new XpTrackerState(
        account.Id, account.Username, account.Session.RatePerHour, account.Session.SessionGain,
        account.Session.ActiveClassName, account.Session.XpUntilNextLevel,
        account.Session.HoursUntilNextLevel,
        account.Session.LastSuccessfulAt, StatusOf(account), account.Session.IsStale)).ToArray();

    // While the live game feed watches the account, the two ordinary statuses say so. Problems still show as they are.
    private static string StatusOf(TrackedAccount account) =>
        account.Session.IsLive && account.Status is "Tracking" or "Collecting baseline"
            ? (account.Session.RatePerHour is null ? "Live; collecting a minute first" : "Tracking live")
            : account.Status;

    public IReadOnlyList<(Guid AccountId, int PlayerId, string Username)> GetActiveLeaderboardProfiles()
    {
        _dispatcher.VerifyAccess();
        return _active.Values
            .Where(account => account.PlayerId is > 0 && !string.IsNullOrWhiteSpace(account.Username))
            .Select(account => (account.Id, account.PlayerId!.Value, account.Username!))
            .ToArray();
    }

    // The newest profile fetched this session for an open account, with the live game feed's last fight laid over it;
    // null until its first successful fetch.
    public PlayerProgressSnapshot? GetLatestSnapshot(Guid accountId) =>
        _active.TryGetValue(accountId, out var account) ? account.Session.DisplaySnapshot : null;

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
            account.Session.ResetRate(DateTimeOffset.UtcNow);
            NotifyChanged();
        }
    }

    public void ResetAll(Guid accountId)
    {
        _dispatcher.VerifyAccess();
        if (_active.TryGetValue(accountId, out var account))
        {
            account.Session.ResetAll(DateTimeOffset.UtcNow);
            NotifyChanged();
        }
    }

    // The live game feed's calls, made on the UI thread like the poll's. An account that isn't tracked is ignored: its
    // panel may have closed while the call was on its way.
    public void BeginLive(Guid accountId, DateTimeOffset at) => Live(accountId, session => session.BeginLive(at));

    public void EndLive(Guid accountId, DateTimeOffset at) => Live(accountId, session => session.EndLive(at));

    public void ApplyLiveResult(
        Guid accountId, DateTimeOffset at, string? className, long expGained, int reachedLevel, long expNeededToNextLevel) =>
        Live(accountId, session => session.ApplyLiveResult(at, className, expGained, reachedLevel, expNeededToNextLevel));

    private void Live(Guid accountId, Action<XpTrackingSession> change)
    {
        _dispatcher.VerifyAccess();
        if (_active.TryGetValue(accountId, out var account))
        {
            change(account.Session);
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
                account.Session.MarkFetchFailed();
                account.Status = "Tracker unavailable; retry when a client is reopened";
            }
            NotifyChanged();
        }
    }

    internal async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            foreach (var (id, entry) in await _store.LoadAsync(cancellationToken)) _stored[id] = entry;
            _loaded = true;
        }

        using var gate = new SemaphoreSlim(2, 2);
        var tasks = _active.Values.ToArray().Select(async account =>
        {
            await gate.WaitAsync(cancellationToken);
            try { await PollAccountAsync(account, cancellationToken); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task PollAccountAsync(TrackedAccount account, CancellationToken cancellationToken)
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
            {
                playerId = cached.PlayerId;
            }

            var result = await _profileService.ReadAsync(account.Id, account.Username, playerId, cancellationToken);
            if (!result.IsSuccess && account.CanRetryProfileRead &&
                result.Status is PlayerProfileReadStatus.RankingUnavailable or
                    PlayerProfileReadStatus.ProfileUnavailable or PlayerProfileReadStatus.MalformedProfile)
            {
                account.CanRetryProfileRead = false;
                await Task.Delay(ProfileRetryDelay, cancellationToken);
                if (!_active.TryGetValue(account.Id, out current) || !ReferenceEquals(current, account)) return;
                result = await _profileService.ReadAsync(account.Id, account.Username, playerId, cancellationToken);
            }
            if (!result.IsSuccess)
            {
                if (result.Status == PlayerProfileReadStatus.Ambiguous)
                {
                    account.Status = "Multiple player matches; link a profile";
                }
                else if (result.Status == PlayerProfileReadStatus.NotInTop200)
                {
                    account.Status = "Not in top 200; link a profile";
                }
                else if (result.Status == PlayerProfileReadStatus.ProfileMismatch)
                {
                    account.PlayerId = null;
                    account.Status = "Player name mismatch; check profile link";
                    account.Session.MarkFetchFailed();
                }
                else
                {
                    // A failed read can supply an id the account lacks, but never clears or replaces one.
                    account.PlayerId ??= result.PlayerId;
                    account.Status = "Stale; could not refresh player data";
                    account.Session.MarkFetchFailed();
                }

                NotifyChanged();
                return;
            }

            var profile = result.Snapshot!;
            if (!_active.TryGetValue(account.Id, out current) || !ReferenceEquals(current, account)) return;
            account.CanRetryProfileRead = true;
            account.PlayerId = result.PlayerId;
            var sampledAt = DateTimeOffset.UtcNow;
            account.Session.ApplySnapshot(profile, sampledAt);
            // A handler that throws must not turn a good read into a failed one.
            try { ProfileSampled?.Invoke(account.Id, profile, sampledAt); }
            catch (Exception) { }
            account.Status = account.Session.MissedPreviousSample
                ? "Partial interval; previous tracker sample failed"
                : account.Session.ActiveClassUnavailable
                    ? "Active class unavailable in public profile"
                    : account.Session.InvalidClassNames.Count > 0
                        ? $"Could not compare XP for {account.Session.ActiveClassName}"
                        : account.Session.RatePerHour is null ? "Collecting baseline" : "Tracking";
            _stored[account.Id] = new XpStoredAccount(account.Id, result.PlayerId!.Value, profile,
                account.Session.Intervals.ToArray());
            await _store.SaveAsync(_stored.Values.ToArray(), sampledAt, cancellationToken);
            NotifyChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            account.Session.MarkFetchFailed();
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
        _ownedTransport?.Dispose();
    }

    private sealed class TrackedAccount(Guid id, string? username, int? playerId)
    {
        public Guid Id { get; } = id;
        public TrackedPlayerIdentity Identity { get; } = new(username, playerId);
        public string? Username => Identity.Username;
        public int? PlayerId { get => Identity.ResolvedPlayerId; set => Identity.ResolvedPlayerId = value; }
        public XpTrackingSession Session { get; } = new();
        public string Status { get; set; } = "Collecting baseline";
        public bool CanRetryProfileRead { get; set; } = true;
    }
}
