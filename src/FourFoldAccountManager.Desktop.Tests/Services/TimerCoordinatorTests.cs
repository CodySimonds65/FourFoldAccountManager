using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Timing;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class TimerCoordinatorTests
{
    [Fact]
    public void NewCoordinatorShowsAReadyTimerWithoutTicking() => WpfTestHost.Run(() =>
    {
        using var coordinator = new TimerCoordinator(new ManualTimeProvider());

        Assert.Equal("0:00.00", coordinator.Display.TotalText);
        Assert.Equal("Lap 1  0:00.00", coordinator.Display.LapText);
        Assert.Equal("Start", coordinator.Display.SplitButtonText);
        Assert.Equal(string.Empty, coordinator.Display.Summary);
        Assert.False(coordinator.IsTicking);
    });

    [Fact]
    public void SplitStartsTickingAndRefreshShowsElapsedTime() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(1.25));
        coordinator.Refresh();

        Assert.True(coordinator.IsTicking);
        Assert.Equal(SpeedrunTimerState.Running, coordinator.Display.State);
        Assert.Equal("0:01.25", coordinator.Display.TotalText);
        Assert.Equal("Lap 1  0:01.25", coordinator.Display.LapText);
        Assert.Equal("Split", coordinator.Display.SplitButtonText);
    });

    [Fact]
    public void LapsAppearAndFinishStopsTicking() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(10));
        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(5));
        coordinator.Finish();

        Assert.False(coordinator.IsTicking);
        Assert.Equal(
        [
            new TimerLapRow(1, "0:10.00", "0:10.00"),
            new TimerLapRow(2, "0:05.00", "0:15.00")
        ], coordinator.Display.Laps);
        Assert.Equal("0:15.00", coordinator.Display.TotalText);
        Assert.Equal("Lap 2  0:05.00", coordinator.Display.LapText);
    });

    [Fact]
    public void ResetAfterManyLapsClearsTheListAndTheNextRunStartsAtLapOne() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);
        coordinator.Split();
        for (var lap = 0; lap < 200; lap++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            coordinator.Split();
        }

        Assert.Equal(200, coordinator.Display.Laps.Count);
        coordinator.Reset();
        Assert.Empty(coordinator.Display.Laps);
        Assert.Equal("Start", coordinator.Display.SplitButtonText);
        Assert.False(coordinator.IsTicking);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(1));
        coordinator.Split();
        Assert.Equal([new TimerLapRow(1, "0:01.00", "0:01.00")], coordinator.Display.Laps);
    });

    [Fact]
    public void TimerShortcutsDriveTheTimerAndOtherActionsAreNotHandled() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);

        Assert.True(coordinator.TryHandleShortcut(GlobalShortcutAction.TimerSplit));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(coordinator.TryHandleShortcut(GlobalShortcutAction.TimerFinish));
        Assert.Equal("0:02.00", coordinator.Display.TotalText);
        Assert.True(coordinator.TryHandleShortcut(GlobalShortcutAction.TimerReset));
        Assert.Equal(SpeedrunTimerState.Ready, coordinator.Display.State);

        Assert.False(coordinator.TryHandleShortcut(GlobalShortcutAction.RevealOverlays));
        Assert.False(coordinator.TryHandleShortcut(GlobalShortcutAction.ToggleDividerResizing));
    });

    [Fact]
    public void SuspendedShortcutsDoNotChangeTheRun() => WpfTestHost.Run(() =>
    {
        using var coordinator = new TimerCoordinator(new ManualTimeProvider());
        coordinator.ShortcutsSuspended = true;

        Assert.True(coordinator.TryHandleShortcut(GlobalShortcutAction.TimerSplit));
        Assert.Equal(SpeedrunTimerState.Ready, coordinator.Display.State);

        coordinator.ShortcutsSuspended = false;
        coordinator.TryHandleShortcut(GlobalShortcutAction.TimerSplit);
        coordinator.ShortcutsSuspended = true;
        coordinator.TryHandleShortcut(GlobalShortcutAction.TimerReset);
        Assert.Equal(SpeedrunTimerState.Running, coordinator.Display.State);
    });

    [Fact]
    public void RefreshRaisesPropertyChangedForTheTotal() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);
        var changed = new List<string?>();
        coordinator.Display.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(1));
        coordinator.Refresh();

        Assert.Contains(nameof(TimerDisplay.TotalText), changed);
        Assert.Contains(nameof(TimerDisplay.SplitButtonText), changed);
    });
}
