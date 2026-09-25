using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class OverlayCardLayerTests
{
    private static readonly OverlayAddOnDefinition Xp = OverlayAddOnCatalog.All[0];

    [Fact]
    public void SetCardsAddsUpdatesAndRemovesFramesByKey() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var first = NewKey();
        var second = NewKey();

        layer.SetCards([Model(first, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A"), Model(second, null, "B")], editing: false);

        Assert.Equal(2, layer.Children.Count);
        var frame = layer.Frames[first];
        Assert.Equal(100, Canvas.GetLeft(frame), 3);
        Assert.Equal(50, Canvas.GetTop(frame), 3);
        Assert.Equal(200, frame.Width, 3);
        Assert.Equal(50, frame.Height, 3);
        Assert.False(frame.IsEditing);

        layer.SetCards([Model(first, new OverlayBounds(0.5, 0.5, 0.2, 0.1), "A2")], editing: true);

        Assert.Same(frame, Assert.Single(layer.Children.Cast<UIElement>()));
        Assert.Equal(500, Canvas.GetLeft(frame), 3);
        Assert.Equal(new XpOverlayCardData("A2", "1 XP/hr"), frame.CardData);
        Assert.True(frame.IsEditing);
    });

    [Fact]
    public void CardsWithoutBoundsAreCascadedFromTheDefaultPosition() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var first = NewKey();
        var second = NewKey();

        layer.SetCards([Model(first, null, "A", cascade: 0), Model(second, null, "B", cascade: 1)], editing: false);

        Assert.Equal(12, Canvas.GetLeft(layer.Frames[first]), 3);
        Assert.Equal(28, Canvas.GetLeft(layer.Frames[second]), 3);
        Assert.Equal(28, Canvas.GetTop(layer.Frames[second]), 3);
    });

    [Fact]
    public void CardEnabledBeforeTheLayerIsMeasuredAppearsAtTheDefaultOnceMeasured() => WpfTestHost.Run(() =>
    {
        var layer = new OverlayCardLayer();
        var key = NewKey();

        layer.SetCards([Model(key, null, "A")], editing: false);
        Arrange(layer, 1000, 500);

        Assert.Equal(12, Canvas.GetLeft(layer.Frames[key]), 3);
        Assert.Equal(12, Canvas.GetTop(layer.Frames[key]), 3);
    });

    [Fact]
    public void DraggingCommitsBoundsForTheDraggedCard() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var key = NewKey();
        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A"), Model(NewKey(), null, "B")], editing: true);
        OverlayCardBoundsCommittedEventArgs? committed = null;
        layer.BoundsCommitted += (_, args) => committed = args;

        var frame = layer.Frames[key];
        frame.MoveThumb.RaiseEvent(new DragDeltaEventArgs(50, 25));
        frame.MoveThumb.RaiseEvent(new DragCompletedEventArgs(50, 25, false));

        Assert.NotNull(committed);
        Assert.Equal(key, committed.Key);
        Assert.Equal(0.15, committed.Bounds.X, 6);
        Assert.Equal(0.15, committed.Bounds.Y, 6);
        Assert.Equal(0.2, committed.Bounds.Width, 6);
    });

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
    public void CanceledDragRestoresSavedBoundsWithoutCommitting() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var key = NewKey();
        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A")], editing: true);
        var commits = 0;
        layer.BoundsCommitted += (_, _) => commits++;

        var frame = layer.Frames[key];
        frame.MoveThumb.RaiseEvent(new DragDeltaEventArgs(50, 25));
        frame.MoveThumb.RaiseEvent(new DragCompletedEventArgs(50, 25, true));

        Assert.Equal(0, commits);
        Assert.Equal(100, Canvas.GetLeft(frame), 3);
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

    [Fact]
    public void LayerTakesInputOnlyOnCardsAndOnlyInEditMode() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var key = NewKey();

        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A")], editing: false);
        Assert.False(layer.IsHitTestVisible);
        Assert.False(layer.Frames[key].IsHitTestVisible);

        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A")], editing: true);
        layer.UpdateLayout();
        Assert.True(layer.IsHitTestVisible);
        Assert.NotNull(VisualTreeHelper.HitTest(layer, new Point(150, 70)));
        Assert.Null(VisualTreeHelper.HitTest(layer, new Point(900, 450)));

        layer.SetCards([], editing: true);
        Assert.False(layer.IsHitTestVisible);
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
