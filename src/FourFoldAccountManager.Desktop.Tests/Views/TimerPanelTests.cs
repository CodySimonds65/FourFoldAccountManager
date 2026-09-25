using System.Windows;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class TimerPanelTests
{
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

    // TimerPanel is never connected to a PresentationSource in these tests, so bindings only
    // update once the element has gone through an explicit Measure/Arrange pass.
    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
}
