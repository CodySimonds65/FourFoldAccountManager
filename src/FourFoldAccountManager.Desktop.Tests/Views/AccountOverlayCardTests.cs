using System.Windows;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class AccountOverlayCardTests
{
    private static readonly StatsCardContent StatsContent = StatsCardContent.FromState(
        new ClassComparisonDisplayState(true, "Profile stats loaded.", "Mage", 42, null,
        [
            new ClassStatComparison(CharacterStat.Hp, 1_100, 1_000, 100, 10.0, ComparisonDirection.Above),
            new ClassStatComparison(CharacterStat.Defense, 90, 100, -10, -10.0, ComparisonDirection.Below)
        ], 1, 1, 0, 0.0));

    [Fact]
    public void StatsCardRendersHeaderSummaryAndRows() => WpfTestHost.Run(() =>
    {
        var texts = RenderTexts(OverlayAddOnKind.Stats, new StatsCardData("Alice", false, StatsContent), 1000, 500);

        Assert.Contains("Alice · Mage 42", texts);
        Assert.Contains("▲1 ▼1 · mean 0.0%", texts);
        Assert.Contains("HP", texts);
        Assert.Contains("1,100", texts);
        Assert.Contains("+10.0%", texts);
        Assert.Contains("DEF", texts);
        Assert.Contains("-10.0%", texts);
    });

    [Fact]
    public void XpCalcCardRendersTargetAndRemaining() => WpfTestHost.Run(() =>
    {
        var content = XpCalcCardContent.FromSnapshot(Snapshot(), savedTargetLevel: 3);

        var texts = RenderTexts(OverlayAddOnKind.XpCalc, new XpCalcCardData("Alice", false, content), 1000, 500);

        Assert.Contains("Alice · Warrior 2 → 3", texts);
        Assert.Contains("25 XP · 1 level to go", texts);
    });

    [Fact]
    public void StatsCardHeaderWrapsLongLabelsInsteadOfTrimming() => WpfTestHost.Run(() =>
    {
        var data = new StatsCardData("A very long account label here", true, StatsContent);
        var (layer, key) = RenderCard(OverlayAddOnKind.Stats, data, 1000, 500);

        var header = VisualTree.Descendants<TextBlock>(layer.Frames[key])
            .First(text => text.Text == data.HeaderText);

        Assert.EndsWith("· stale", header.Text);
        Assert.Equal(TextWrapping.Wrap, header.TextWrapping);
        Assert.Equal(TextTrimming.None, header.TextTrimming);
    });

    [Fact]
    public void StaleCardsMarkTheirHeader()
    {
        Assert.Equal("Alice · Mage 42 · stale", new StatsCardData("Alice", true, StatsContent).HeaderText);
        var waiting = new XpCalcCardData("Alice", true, XpCalcCardContent.FromSnapshot(null, null));
        Assert.Equal("Alice · stale", waiting.HeaderText);
        Assert.Equal("Waiting for profile…", waiting.LineText);
    }

    [Fact]
    public void SummariesShowCountsAndTarget()
    {
        Assert.Equal("▲1 ▼1", new StatsCardData("Alice", false, StatsContent).Summary);
        Assert.Equal("→ 3", new XpCalcCardData("Alice", false,
            XpCalcCardContent.FromSnapshot(Snapshot(), 3)).Summary);
        Assert.Equal(string.Empty, new XpCalcCardData("Alice", false,
            XpCalcCardContent.FromSnapshot(null, null)).Summary);
        Assert.Equal(string.Empty, new StatsCardData("Alice", false, StatsCardContent.FromSnapshot(null)).Summary);
    }

    [Theory]
    [InlineData(OverlayAddOnKind.Stats)]
    [InlineData(OverlayAddOnKind.XpCalc)]
    public void CardsShrinkToFitAtTheirMinimumSize(OverlayAddOnKind kind) => WpfTestHost.Run(() =>
    {
        OverlayAddOnCatalog.TryGet(kind, out var definition);
        IOverlayCardData data = kind == OverlayAddOnKind.Stats
            ? new StatsCardData("Alice", false, StatsContent)
            : new XpCalcCardData("Alice", false, XpCalcCardContent.FromSnapshot(Snapshot(), 3));
        var (layer, key) = RenderCard(kind, data, definition!.MinimumWidth, definition.MinimumHeight);

        var frame = layer.Frames[key];
        var viewbox = Assert.Single(VisualTree.Descendants<Viewbox>(frame));
        var child = Assert.IsAssignableFrom<FrameworkElement>(viewbox.Child);

        Assert.True(viewbox.ActualWidth <= frame.ActualWidth && viewbox.ActualHeight <= frame.ActualHeight);
        Assert.True(viewbox.ActualWidth < child.DesiredSize.Width || viewbox.ActualHeight < child.DesiredSize.Height,
            $"Expected the {kind} card content ({child.DesiredSize}) to shrink into {viewbox.ActualWidth}x{viewbox.ActualHeight}.");
    });

    private static string[] RenderTexts(OverlayAddOnKind kind, IOverlayCardData data, double width, double height)
    {
        var (layer, key) = RenderCard(kind, data, width, height);
        return VisualTree.Descendants<TextBlock>(layer.Frames[key]).Select(text => text.Text).ToArray();
    }

    private static (OverlayCardLayer Layer, OverlayCardKey Key) RenderCard(
        OverlayAddOnKind kind, IOverlayCardData data, double width, double height)
    {
        OverlayAddOnCatalog.TryGet(kind, out var definition);
        var layer = new OverlayCardLayer { Width = width, Height = height };
        layer.Measure(new Size(width, height));
        layer.Arrange(new Rect(0, 0, width, height));
        layer.UpdateLayout();
        var key = new OverlayCardKey(kind, Guid.NewGuid());
        layer.SetCards([new OverlayCardModel(key, definition!, null, 0, data)], editing: false);
        layer.UpdateLayout();
        return (layer, key);
    }

    private static PlayerProgressSnapshot Snapshot() =>
        new("Alice", "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, 5, 30, null) { ClassName = "Warrior" }
        }, []);
}
