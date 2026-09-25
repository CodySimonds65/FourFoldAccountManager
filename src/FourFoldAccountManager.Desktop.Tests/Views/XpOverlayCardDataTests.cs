using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class XpOverlayCardDataTests
{
    [Fact]
    public void SummaryIsTheXpPerHourText()
    {
        IOverlayCardData data = new XpOverlayCardData("Alice", "12 XP/hr");

        Assert.Equal("12 XP/hr", data.Summary);
    }
}
