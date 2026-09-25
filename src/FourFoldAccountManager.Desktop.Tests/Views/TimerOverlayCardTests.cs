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
        Assert.Equal(12, Canvas.GetTop(layer.Frames[key]), 3);
    });

    [Fact]
    public void TimerCardContentFitsAtItsMinimumSize() => WpfTestHost.Run(() =>
    {
        using var coordinator = new TimerCoordinator(new ManualTimeProvider());
        OverlayAddOnCatalog.TryGet(OverlayAddOnKind.Timer, out var definition);
        var layer = new OverlayCardLayer { Width = definition!.MinimumWidth, Height = definition.MinimumHeight };
        layer.Measure(new Size(definition.MinimumWidth, definition.MinimumHeight));
        layer.Arrange(new Rect(0, 0, definition.MinimumWidth, definition.MinimumHeight));
        layer.UpdateLayout();
        var key = new OverlayCardKey(OverlayAddOnKind.Timer, null);

        layer.SetCards([new OverlayCardModel(key, definition, null, 0, coordinator.Display)], editing: false);
        layer.UpdateLayout();

        var frame = layer.Frames[key];
        Assert.Equal(definition.MinimumWidth, frame.Width, 3);
        Assert.Equal(definition.MinimumHeight, frame.Height, 3);

        var viewbox = FindViewbox(frame);
        Assert.NotNull(viewbox);
        var child = Assert.IsAssignableFrom<FrameworkElement>(viewbox!.Child);

        // Viewbox reports its own (shrunk) allocated size while its child keeps reporting its natural,
        // unscaled size (LayoutTransform scaling is applied beneath the layout system, not to ActualHeight).
        // A rendered height smaller than the child's natural height is exactly the shrink-to-fit behavior
        // this template relies on to avoid clipping at the card's minimum size.
        Assert.True(viewbox.ActualHeight > 0 && viewbox.ActualHeight < child.DesiredSize.Height,
            $"Expected the Timer card content ({child.DesiredSize.Height:F1}px tall) to be shrunk to fit the " +
            $"card's available space ({viewbox.ActualHeight:F1}px), so it isn't clipped at the minimum card size.");
    });

    private static Viewbox? FindViewbox(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Viewbox viewbox)
            {
                return viewbox;
            }

            if (FindViewbox(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

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
