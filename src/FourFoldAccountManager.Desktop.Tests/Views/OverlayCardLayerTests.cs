using System.Windows;
using System.Windows.Controls.Primitives;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class OverlayCardLayerTests
{
    private static readonly OverlayAddOnDefinition Xp = OverlayAddOnCatalog.All[0];

    [Fact]
    public void DraggingFlushToTheRightEdgeCommitsValidBounds() => WpfTestHost.Run(() =>
    {
        // These concrete dimensions reproduce a real edge-drag: dividing the flush-right left
        // offset and the card width by ActualWidth independently rounds their sum to
        // 1.0000000000000002, so the raw (pre-fix) bounds fail IsValid.
        const double layerWidth = 1436.8036476135271;
        const double cardWidthFraction = 0.23263519398525134;
        var layer = MeasuredLayer(layerWidth, 500);
        var key = NewKey();
        layer.SetCards([Model(key, new OverlayBounds(0.05, 0.05, cardWidthFraction, 0.1), "A")], editing: true);
        OverlayCardBoundsCommittedEventArgs? committed = null;
        layer.BoundsCommitted += (_, args) => committed = args;

        var frame = layer.Frames[key];
        frame.MoveThumb.RaiseEvent(new DragDeltaEventArgs(1_000_000, 0));
        frame.MoveThumb.RaiseEvent(new DragCompletedEventArgs(1_000_000, 0, false));

        Assert.NotNull(committed);
        Assert.True(committed.Bounds.IsValid);
        Assert.True(committed.Bounds.X + committed.Bounds.Width <= 1d);
    });

    [Fact]
    public void CompletingADragAfterTheCardWasRemovedCommitsNothing() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var key = NewKey();
        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A")], editing: true);
        var commits = 0;
        layer.BoundsCommitted += (_, _) => commits++;

        var frame = layer.Frames[key];
        frame.MoveThumb.RaiseEvent(new DragDeltaEventArgs(50, 25));
        layer.SetCards([], editing: false);
        frame.MoveThumb.RaiseEvent(new DragCompletedEventArgs(50, 25, false));

        Assert.Equal(0, commits);
        Assert.Empty(layer.Children);
    });

    private static OverlayCardKey NewKey() => new(OverlayAddOnKind.Xp, Guid.NewGuid());

    private static OverlayCardModel Model(OverlayCardKey key, OverlayBounds? bounds, string label, int cascade = 0) =>
        new(key, Xp, bounds, cascade, new XpOverlayCardData(label, "1 XP/hr"));

    private static OverlayCardLayer MeasuredLayer(double width, double height)
    {
        var layer = new OverlayCardLayer();
        Arrange(layer, width, height);
        return layer;
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
}
