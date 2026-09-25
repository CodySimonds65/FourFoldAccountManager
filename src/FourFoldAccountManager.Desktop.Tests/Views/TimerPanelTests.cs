using System.Windows;
using System.Windows.Controls.Primitives;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class TimerPanelTests
{
    [Fact]
    public void SplitButtonLabelFollowsTheTimerState() => WpfTestHost.Run(() =>
    {
        using var coordinator = new TimerCoordinator(new ManualTimeProvider());
        var panel = new TimerPanel();
        panel.Attach(coordinator);
        Arrange(panel, 300, 500);

        Assert.Equal("Start", panel.SplitButton.Content);
        panel.SplitButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        panel.UpdateLayout();
        Assert.Equal("Split", panel.SplitButton.Content);
        panel.ResetButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        panel.UpdateLayout();
        Assert.Equal("Start", panel.SplitButton.Content);
    });

    [Fact]
    public void TotalTextFollowsTheLiveDisplay() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);
        var panel = new TimerPanel();
        panel.Attach(coordinator);

        Arrange(panel, 300, 500);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(3.5));
        coordinator.Refresh();
        panel.UpdateLayout();

        Assert.Equal("0:03.50", panel.TotalText.Text);
        Assert.Equal("Lap 1  0:03.50", panel.LapText.Text);
    });

    [Fact]
    public void RecordingALapScrollsTheLapsListWithoutThrowing() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);
        var panel = new TimerPanel();
        panel.Attach(coordinator);
        Arrange(panel, 300, 500);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(1));
        coordinator.Split();
        panel.UpdateLayout();

        Assert.Single(coordinator.Display.Laps);
    });

    [Fact]
    public void SplitFinishAndResetButtonsAreNotFocusable() => WpfTestHost.Run(() =>
    {
        var panel = new TimerPanel();

        Assert.False(panel.SplitButton.Focusable);
        Assert.False(panel.FinishButton.Focusable);
        Assert.False(panel.ResetButton.Focusable);
    });

    [Fact]
    public void HotkeysLineAndUnavailableNote() => WpfTestHost.Run(() =>
    {
        var panel = new TimerPanel();

        panel.SetHotkeys("Num 1", "Num 2", "Num 3", anyUnavailable: false);
        Assert.Equal("Split Num 1 · Finish Num 2 · Reset Num 3", panel.HotkeyText.Text);
        Assert.Equal(Visibility.Collapsed, panel.UnavailableNote.Visibility);

        panel.SetHotkeys("Num 1", "Num 2", "Num 3", anyUnavailable: true);
        Assert.Equal(Visibility.Visible, panel.UnavailableNote.Visibility);
    });

    // TimerPanel is never connected to a PresentationSource in these tests, so bindings only
    // update once the element has gone through an explicit Measure/Arrange pass.
    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
}
