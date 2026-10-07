using System.Text.Json;
using System.Threading.Channels;
using System.Windows.Threading;
using FourFoldAccountManager.Core.LiveFeed;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Core.Tracking;
using Microsoft.Web.WebView2.Core;

namespace FourFoldAccountManager.Desktop.Services.LiveFeed;

internal sealed record LiveFeedCounters(
    long Frames, long Recognized, long Dropped, long Failures, int Matched, int Mismatched, int Skipped);

internal enum LiveFeedState
{
    Off,
    Active,
    Unavailable
}

// The live game feed. It taps each game panel's socket (read-only), decodes the allowlisted messages off the UI thread,
// keeps each account's current scene, and passes events to plugins. Nothing built into the app depends on it: when it
// is off or unavailable, everything else carries on from the profile poll.
internal sealed class LiveGameFeed : IAsyncDisposable
{
    public const int QueueCapacity = 4096;
    public static readonly TimeSpan CanaryTimeout = TimeSpan.FromSeconds(60);

    private readonly AccountBrowserSessionService _sessions;
    private readonly XpTrackerCoordinator _tracker;
    private readonly Action<string, object?> _postEvent;
    private readonly Dispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly Channel<TapEvent> _queue;
    private readonly Task _worker;

    // UI thread only.
    private readonly Dictionary<Guid, CoreWebView2> _views = [];
    private readonly Dictionary<Guid, GameFeedTap> _taps = [];
    private bool _enabled;
    private bool _disposed;

    // Under _gate: read by plugin calls on the UI thread, written by the worker.
    private readonly object _gate = new();
    private readonly Dictionary<Guid, AccountState> _accounts = [];
    private readonly XpReconciler _reconciler = new();
    private LiveFeedState _state = LiveFeedState.Off;
    private string? _reason;

    private long _frames;
    private long _recognized;
    private long _dropped;
    private long _failures;

    public LiveGameFeed(
        AccountBrowserSessionService sessions,
        XpTrackerCoordinator tracker,
        Action<string, object?> postEvent,
        Dispatcher dispatcher,
        TimeProvider clock)
    {
        _sessions = sessions;
        _tracker = tracker;
        _postEvent = postEvent;
        _dispatcher = dispatcher;
        _clock = clock;
        // Drop-oldest: the UI thread must never wait on the worker. Every drop is counted.
        _queue = Channel.CreateBounded<TapEvent>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            },
            _ => Interlocked.Increment(ref _dropped));
        _sessions.ViewCreated += OnViewCreated;
        _sessions.ViewClosing += OnViewClosing;
        _tracker.ProfileSampled += OnProfileSampled;
        _worker = Task.Run(RunAsync);
    }

    // UI thread. Turning the feed on applies to a panel the next time its game connects: the tap must see the socket
    // being created to know which socket is the game's.
    public void SetEnabled(bool enabled)
    {
        if (_disposed || enabled == _enabled)
        {
            return;
        }

        _enabled = enabled;
        if (enabled)
        {
            foreach (var (accountId, core) in _views)
            {
                Attach(accountId, core);
            }
        }
        else
        {
            foreach (var tap in _taps.Values)
            {
                tap.Detach();
            }

            _taps.Clear();
        }

        LiveStatus status;
        lock (_gate)
        {
            EndAllLive();
            _state = enabled ? LiveFeedState.Active : LiveFeedState.Off;
            _reason = null;
            status = CurrentStatus();
        }

        Post("live.statusChanged", status);
    }

    public LiveStatus GetStatus()
    {
        lock (_gate)
        {
            return CurrentStatus();
        }
    }

    public LiveLocation? GetLocation(Guid accountId)
    {
        lock (_gate)
        {
            return _state == LiveFeedState.Active && _accounts.TryGetValue(accountId, out var account)
                ? account.Location
                : null;
        }
    }

    public LiveFeedCounters GetCounters()
    {
        lock (_gate)
        {
            return new LiveFeedCounters(
                Interlocked.Read(ref _frames), Interlocked.Read(ref _recognized), Interlocked.Read(ref _dropped),
                Interlocked.Read(ref _failures), _reconciler.Matched, _reconciler.Mismatched, _reconciler.Skipped);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessions.ViewCreated -= OnViewCreated;
        _sessions.ViewClosing -= OnViewClosing;
        _tracker.ProfileSampled -= OnProfileSampled;
        foreach (var tap in _taps.Values)
        {
            tap.Detach();
        }

        _taps.Clear();
        _queue.Writer.TryComplete();
        await _worker;
    }

    private void OnViewCreated(Guid accountId, CoreWebView2 core)
    {
        _views[accountId] = core;
        if (_enabled)
        {
            Attach(accountId, core);
        }
    }

    private void OnViewClosing(Guid accountId)
    {
        _views.Remove(accountId);
        if (_taps.Remove(accountId, out var tap))
        {
            tap.Detach();
        }

        Enqueue(new TapEvent(accountId, TapEventKind.ViewClosed, string.Empty, _clock.GetUtcNow()));
    }

    private void OnProfileSampled(Guid accountId, PlayerProgressSnapshot snapshot, DateTimeOffset sampledAt)
    {
        bool resultsStopped;
        lock (_gate)
        {
            // The window only counts when the feed watched this account's game socket for all of it.
            var watchedSince = _state == LiveFeedState.Active && _accounts.TryGetValue(accountId, out var account)
                ? account.OpenedAt
                : (DateTimeOffset?)null;
            resultsStopped = _reconciler.RecordSample(accountId, snapshot, sampledAt, watchedSince);
        }

        // A game update can change only the battle result: login and scene still decode, so nothing else notices.
        // MarkUnavailable takes _gate itself.
        if (resultsStopped)
        {
            MarkUnavailable("Battle results stopped arriving. The game may have updated.");
        }
    }

    private void Attach(Guid accountId, CoreWebView2 core)
    {
        var tap = new GameFeedTap(core, accountId, Enqueue, _clock);
        _taps[accountId] = tap;
        _ = AttachAsync(accountId, tap);
    }

    private async Task AttachAsync(Guid accountId, GameFeedTap tap)
    {
        try
        {
            await tap.AttachAsync();
        }
        catch (Exception)
        {
            // Resumes on the UI thread, where _taps lives. A panel closed mid-attach isn't a feed failure.
            if (_taps.TryGetValue(accountId, out var current) && ReferenceEquals(current, tap))
            {
                MarkUnavailable("The live feed couldn't attach to a game panel.");
            }
        }
    }

    private void Enqueue(TapEvent tapEvent) => _queue.Writer.TryWrite(tapEvent);

    private async Task RunAsync()
    {
        await foreach (var tapEvent in _queue.Reader.ReadAllAsync())
        {
            try
            {
                Handle(tapEvent);
            }
            catch (Exception)
            {
                // One bad event must never stop the feed. Nothing is logged: the event holds game traffic.
            }
        }
    }

    private void Handle(TapEvent tapEvent)
    {
        switch (tapEvent.Kind)
        {
            case TapEventKind.SocketCreated:
                OnSocketCreated(tapEvent);
                break;
            case TapEventKind.Frame:
                OnFrame(tapEvent);
                break;
            case TapEventKind.SocketClosed:
                OnSocketClosed(tapEvent.AccountId, RequestId(tapEvent.Json), tapEvent.At);
                break;
            case TapEventKind.ViewClosed:
                OnSocketClosed(tapEvent.AccountId, requestId: null, tapEvent.At);
                break;
        }
    }

    private void OnSocketCreated(TapEvent tapEvent)
    {
        using var document = JsonDocument.Parse(tapEvent.Json);
        var root = document.RootElement;
        if (!root.TryGetProperty("requestId", out var id) || id.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String ||
            !IsGameSocket(url.GetString()))
        {
            return;
        }

        lock (_gate)
        {
            if (_state == LiveFeedState.Active)
            {
                // A new game socket (a login or a reconnect) starts the account's state and its canary afresh.
                _accounts[tapEvent.AccountId] = new AccountState(id.GetString()!, tapEvent.At);
                Tell(tracker => tracker.BeginLive(tapEvent.AccountId, tapEvent.At));
            }
        }
    }

    private void OnFrame(TapEvent tapEvent)
    {
        using var document = JsonDocument.Parse(tapEvent.Json);
        var root = document.RootElement;
        var requestId = root.TryGetProperty("requestId", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
        // Opcode 2 is a binary frame; Mirror sends nothing else on the game socket.
        if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("opcode", out var opcode) || !opcode.TryGetInt32(out var op) || op != 2 ||
            !response.TryGetProperty("payloadData", out var data) || data.ValueKind != JsonValueKind.String)
        {
            return;
        }

        // Only the account's game socket, and only while the feed is active; other frames aren't even decoded.
        lock (_gate)
        {
            if (_state != LiveFeedState.Active || !_accounts.TryGetValue(tapEvent.AccountId, out var watched) ||
                watched.SocketId != requestId)
            {
                return;
            }
        }

        Interlocked.Increment(ref _frames);
        byte[] batch;
        try
        {
            batch = Convert.FromBase64String(data.GetString()!);
        }
        catch (FormatException)
        {
            Interlocked.Increment(ref _failures);
            return;
        }

        var result = GameMessageDecoder.DecodeBatch(batch);
        Interlocked.Add(ref _recognized, result.Events.Count);
        Interlocked.Add(ref _failures, result.Failures + result.Malformed);
        if (result.Failures > 0)
        {
            MarkUnavailable("A game message didn't match the expected format. The game may have updated.");
            return;
        }

        var posts = new List<(string Name, object Data)>();
        var results = new List<BattleResult>();
        bool canaryFailed;
        lock (_gate)
        {
            if (_state != LiveFeedState.Active || !_accounts.TryGetValue(tapEvent.AccountId, out var account) ||
                account.SocketId != requestId)
            {
                return;
            }

            foreach (var gameEvent in result.Events)
            {
                switch (gameEvent)
                {
                    case LoggedIn login:
                        account.SawLogin = true;
                        account.LoginPosted |= login.Success;
                        break;
                    case SceneLoaded scene:
                        account.SawScene = true;
                        account.Location = LiveFeedPayloads.Location(tapEvent.AccountId, scene.Scene, tapEvent.At);
                        break;
                    case BattleResult battle:
                        _reconciler.RecordResult(tapEvent.AccountId, tapEvent.At, battle.ClassName, battle.ExpGained);
                        results.Add(battle);
                        break;
                    case ModerationDisconnected:
                        account.DisconnectPosted = true;
                        break;
                }

                if (LiveFeedPayloads.ForPlugins(tapEvent.AccountId, gameEvent, tapEvent.At) is { } post)
                {
                    posts.Add(post);
                }
            }

            // Every login sends both; 60 s of traffic without them means the ids no longer match the game.
            canaryFailed = !(account.SawLogin && account.SawScene) && tapEvent.At - account.OpenedAt > CanaryTimeout;
            if (!canaryFailed)
            {
                foreach (var battle in results)
                {
                    Tell(tracker => tracker.ApplyLiveResult(tapEvent.AccountId, tapEvent.At,
                        PluginText.PublicGameText(battle.ClassName), battle.ExpGained, battle.ReachedLevel,
                        battle.ExpNeededToNextLevel));
                }
            }
        }

        if (canaryFailed)
        {
            MarkUnavailable("Game protocol changed: the live feed didn't recognize the login. The game may have updated.");
            return;
        }

        foreach (var (name, payload) in posts)
        {
            Post(name, payload);
        }
    }

    // requestId is null when the whole view closed: whatever socket the account had is gone.
    private void OnSocketClosed(Guid accountId, string? requestId, DateTimeOffset at)
    {
        bool post;
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var account) ||
                (requestId is not null && account.SocketId != requestId))
            {
                return;
            }

            _accounts.Remove(accountId);
            Tell(tracker => tracker.EndLive(accountId, at));
            // A server kick already told plugins why; the socket closing after it isn't a second disconnect. Plugins
            // weren't told this session started (no successful login), so they aren't told it ended.
            post = _state == LiveFeedState.Active && account.LoginPosted && !account.DisconnectPosted;
        }

        if (post)
        {
            Post("session.disconnected", LiveFeedPayloads.Disconnected(accountId, at));
        }
    }

    private void MarkUnavailable(string reason)
    {
        LiveStatus status;
        lock (_gate)
        {
            if (_state != LiveFeedState.Active)
            {
                return;
            }

            _state = LiveFeedState.Unavailable;
            _reason = reason;
            EndAllLive();
            status = CurrentStatus();
        }

        Post("live.statusChanged", status);
    }

    private LiveStatus CurrentStatus() => new(
        _state switch
        {
            LiveFeedState.Active => "active",
            LiveFeedState.Off => "off",
            _ => "unavailable"
        },
        _reason);

    // Plugin pages are posted to from the UI thread, the only thread WebView2 accepts.
    private void Post(string name, object? data) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                _postEvent(name, data);
            }
        });

    // Under _gate: stops watching every account and tells the tracker.
    private void EndAllLive()
    {
        var at = _clock.GetUtcNow();
        foreach (var accountId in _accounts.Keys)
        {
            Tell(tracker => tracker.EndLive(accountId, at));
        }

        _accounts.Clear();
    }

    // The XP tracker lives on the UI thread, like the poll that feeds it. Called under _gate so the tracker sees calls in
    // the order the feed's state changed; InvokeAsync only queues, so it can't block there.
    private void Tell(Action<XpTrackerCoordinator> call) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                call(_tracker);
            }
        });

    private static bool IsGameSocket(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "wss" &&
        string.Equals(uri.IdnHost, "fourfoldonline.com", StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort &&
        uri.AbsolutePath.TrimEnd('/') == "/game-ws";

    private static string? RequestId(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("requestId", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }

    private sealed class AccountState(string socketId, DateTimeOffset openedAt)
    {
        public string SocketId { get; } = socketId;

        public DateTimeOffset OpenedAt { get; } = openedAt;

        public bool SawLogin { get; set; }

        public bool LoginPosted { get; set; }

        public bool SawScene { get; set; }

        public bool DisconnectPosted { get; set; }

        public LiveLocation? Location { get; set; }
    }
}
