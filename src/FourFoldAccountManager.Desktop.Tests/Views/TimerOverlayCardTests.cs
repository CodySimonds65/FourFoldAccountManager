using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class TimerOverlayCardTests
{
    [Fact]
    public void TimerCardRendersTheLiveTotalAndLapLineAtTopCentre() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);
        OverlayAddOnCatalog.TryGet(OverlayAddOnKind.Timer, out var definition);
        var layer = new OverlayCardLayer { Width = 1000, Height = 500 };
        layer.Measure(new Size(1000, 500));
        layer.Arrange(new Rect(0, 0, 1000, 500));
        layer.UpdateLayout();
        var key = new OverlayCardKey(OverlayAddOnKind.Timer, null);

        layer.SetCards([new OverlayCardModel(key, definition!, null, 0, coordinator.Display)], editing: false);
        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(2.5));
        coordinator.Refresh();
        layer.UpdateLayout();

        var texts = TextBlocks(layer.Frames[key]).Select(text => text.Text).ToArray();
        Assert.Contains("0:02.50", texts);
        Assert.Contains("Lap 1  0:02.50", texts);
        Assert.Equal(390, Canvas.GetLeft(layer.Frames[key]), 3);
    });

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock text)
            {
                yield return text;
            }

            foreach (var nested in TextBlocks(child))
            {
                yield return nested;
            }
        }
    }
}
