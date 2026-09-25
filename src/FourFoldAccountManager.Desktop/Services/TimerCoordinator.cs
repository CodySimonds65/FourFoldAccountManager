using System.Windows.Threading;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Timing;
using FourFoldAccountManager.Desktop.Views;

namespace FourFoldAccountManager.Desktop.Services;

// Owns the app's single speedrun timer. Create it on the UI thread; it ticks only while a run is timing.
public sealed class TimerCoordinator : IDisposable
{
    private readonly SpeedrunTimer _timer;
    private readonly DispatcherTimer _ticker;

    public TimerCoordinator(TimeProvider? clock = null)
    {
        _timer = new SpeedrunTimer(clock);
        _ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _ticker.Tick += (_, _) => Refresh();
        Refresh();
    }

    public TimerDisplay Display { get; } = new();

    // Set while the Settings dialog records shortcuts so a key press cannot change the run.
    public bool ShortcutsSuspended { get; set; }

    internal bool IsTicking => _ticker.IsEnabled;

    public void Split()
    {
        if (_timer.Split())
        {
            OnStateChanged();
        }
    }

    public void Finish()
    {
        if (_timer.Finish())
        {
            OnStateChanged();
        }
    }

    public void Reset()
    {
        _timer.Reset();
        OnStateChanged();
    }

    // Returns true for timer actions, whether or not they ran; other actions belong to the caller.
    public bool TryHandleShortcut(GlobalShortcutAction action)
    {
        switch (action)
        {
            case GlobalShortcutAction.TimerSplit:
                if (!ShortcutsSuspended)
                {
                    Split();
                }

                return true;
            case GlobalShortcutAction.TimerFinish:
                if (!ShortcutsSuspended)
                {
                    Finish();
                }

                return true;
            case GlobalShortcutAction.TimerReset:
                if (!ShortcutsSuspended)
                {
                    Reset();
                }

                return true;
            default:
                return false;
        }
    }

    // Display values are recomputed from the timer on each tick, never accumulated.
    internal void Refresh() => Display.Apply(_timer.Snapshot());

    public void Dispose() => _ticker.Stop();

    private void OnStateChanged()
    {
        Refresh();
        _ticker.IsEnabled = _timer.State == SpeedrunTimerState.Running;
    }
}
