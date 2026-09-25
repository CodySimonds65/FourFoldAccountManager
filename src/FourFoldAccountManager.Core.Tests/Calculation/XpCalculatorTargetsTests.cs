using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Calculation;

public sealed class XpCalculatorTargetsTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Fact]
    public void WithTargetAddsReplacesAndRemovesOnlyThatAccount()
    {
        var settings = XpCalculatorTargets.WithTarget(PanelSettings.Default, Alice, 50);
        settings = XpCalculatorTargets.WithTarget(settings, Bob, 20);
        settings = XpCalculatorTargets.WithTarget(settings, Alice, 60);

        Assert.Equal(60, XpCalculatorTargets.Get(settings, Alice));
        Assert.Equal(20, XpCalculatorTargets.Get(settings, Bob));

        settings = XpCalculatorTargets.WithTarget(settings, Alice, null);

        Assert.Null(XpCalculatorTargets.Get(settings, Alice));
        Assert.Equal(20, XpCalculatorTargets.Get(settings, Bob));
    }

}
