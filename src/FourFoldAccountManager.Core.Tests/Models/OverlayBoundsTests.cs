using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Models;

public sealed class OverlayBoundsTests
{
    // These concrete doubles reproduce a real edge-drag: X and Width are each valid on their own
    // (X = 0.891458707811059, Width = 0.10854129218894118) but their sum rounds to
    // 1.0000000000000002, so the raw bounds are already invalid before ClampToViewport runs.
    [Fact]
    public void InputWhoseWidthRoundsTheSumAboveOneIsInvalidBeforeClamping()
    {
        var bounds = new OverlayBounds(0.891458707811059, 0.1, 0.10854129218894118, 0.1);

        Assert.True(bounds.X + bounds.Width > 1d);
        Assert.False(bounds.IsValid);
    }

    [Fact]
    public void ClampToViewportOutputIsAlwaysValidEvenWhenTheSumRoundsAboveOne()
    {
        var bounds = new OverlayBounds(0.891458707811059, 0.1, 0.10854129218894118, 0.1);

        var clamped = bounds.ClampToViewport();

        Assert.True(clamped.IsValid);
    }

    [Fact]
    public void ClampToViewportOutputIsAlwaysValidForTheBottomEdgeCase()
    {
        var bounds = new OverlayBounds(0.1, 0.891458707811059, 0.1, 0.10854129218894118);

        var clamped = bounds.ClampToViewport();

        Assert.True(clamped.IsValid);
    }
}
