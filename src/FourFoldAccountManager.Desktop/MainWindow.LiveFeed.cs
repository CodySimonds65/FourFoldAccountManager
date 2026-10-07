using System.Windows.Threading;
using FourFoldAccountManager.Core.LiveFeed;
using FourFoldAccountManager.Desktop.Plugins.Web;
using FourFoldAccountManager.Desktop.Services.LiveFeed;

namespace FourFoldAccountManager.Desktop;

// The live game feed: built once, switched on or off from Settings, read by plugins.
public partial class MainWindow
{
    private LiveGameFeed _liveFeed = null!;
    private DispatcherTimer? _liveRedraw;

    // Starts off; MainWindow_Loaded switches it to the saved setting. Panels created before then are remembered and
    // tapped when it switches on.
    private void InitializeLiveFeed()
    {
        _liveFeed = new LiveGameFeed(
            _browserSessions, _xpTracker, (name, data) => _communityPlugins.PostEvent(name, data), Dispatcher,
            TimeProvider.System);
        // A live XP/hr falls while the account idles; redraw the panel and cards so it shows, not only at each poll.
        _liveRedraw = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) =>
        {
            if (!_shutdownStarted && _xpTracker.HasLiveAccount)
            {
                RefreshTrackerRows();
            }
        }, Dispatcher);
    }

    // One line for Settings: the state, then the counts the feed must earn before the XP tracker relies on it.
    private string LiveFeedSummary()
    {
        var status = _liveFeed.GetStatus();
        var counters = _liveFeed.GetCounters();
        var state = status.State switch
        {
            "active" => "Active",
            "off" => "Off",
            _ => $"Unavailable: {status.Reason}"
        };
        return $"{state} · {counters.Frames:N0} frames · {counters.Recognized:N0} events · " +
               $"{counters.Dropped:N0} dropped · {counters.Failures:N0} errors · XP checks: " +
               $"{counters.Matched:N0} matched, {counters.Mismatched:N0} mismatched, {counters.Skipped:N0} skipped";
    }

    LiveLocation? IPluginHostData.GetLocation(Guid accountId) => _liveFeed.GetLocation(accountId);

    LiveStatus IPluginHostData.GetLiveStatus() => _liveFeed.GetStatus();
}
