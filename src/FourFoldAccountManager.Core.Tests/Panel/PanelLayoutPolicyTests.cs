using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Panel;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Panel;

public sealed class PanelLayoutPolicyTests
{
    [Fact]
    public void SettingsTransformsPreserveLeaderboardSharingPreference()
    {
        var accountId = Guid.NewGuid();
        var settings = PanelSettings.Default with
        {
            ShareLinkedAccounts = true,
            SlotAccountIds = [accountId, null, null, null, null]
        };

        Assert.True(PanelLayoutPolicy.WithLayout(settings, PanelLayout.OneByTwo).ShareLinkedAccounts);
        Assert.True(PanelLayoutPolicy.Assign(settings, 1, Guid.NewGuid()).ShareLinkedAccounts);
        Assert.True(PanelLayoutPolicy.ClearAccount(settings, accountId).ShareLinkedAccounts);
        Assert.True(PanelLayoutPolicy.WithSplitState(settings,
            new PanelSplitState("2x2.rows", [0.7, 0.3])).ShareLinkedAccounts);
        Assert.True(PanelLayoutPolicy.ResetSplitStates(settings).ShareLinkedAccounts);
    }

    [Fact]
    public void SettingsTransformsPreserveOverlayCardsAndClearAccountRemovesTheAccountsCards()
    {
        var accountId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var cards = new[]
        {
            new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, new OverlayBounds(0.1, 0.1, 0.2, 0.1)),
            new OverlayCardPlacement(OverlayAddOnKind.Xp, otherId, false, null)
        };
        var settings = PanelSettings.Default with
        {
            SlotAccountIds = [accountId, otherId, null, null, null],
            OverlayCards = cards
        };

        Assert.Equal(cards, PanelLayoutPolicy.WithLayout(settings, PanelLayout.OneByTwo).OverlayCards);
        Assert.Equal(cards, PanelLayoutPolicy.Assign(settings, 2, Guid.NewGuid()).OverlayCards);
        Assert.Equal(cards, PanelLayoutPolicy.WithSplitState(settings,
            new PanelSplitState("2x2.rows", [0.7, 0.3])).OverlayCards);
        Assert.Equal(cards, PanelLayoutPolicy.ResetSplitStates(settings).OverlayCards);
        Assert.Equal([cards[1]], PanelLayoutPolicy.ClearAccount(settings, accountId).OverlayCards);
    }

    [Fact]
    public void SettingsTransformsPreserveTimerShortcuts()
    {
        var accountId = Guid.NewGuid();
        var settings = PanelSettings.Default with
        {
            SlotAccountIds = [accountId, null, null, null, null],
            TimerSplitShortcut = new GlobalHotkeyChord(0x61, GlobalHotkeyModifiers.None),
            TimerFinishShortcut = new GlobalHotkeyChord(0x62, GlobalHotkeyModifiers.None),
            TimerResetShortcut = new GlobalHotkeyChord(0x63, GlobalHotkeyModifiers.None)
        };

        foreach (var transformed in new[]
                 {
                     PanelLayoutPolicy.WithLayout(settings, PanelLayout.OneByTwo),
                     PanelLayoutPolicy.Assign(settings, 1, Guid.NewGuid()),
                     PanelLayoutPolicy.ClearAccount(settings, accountId),
                     PanelLayoutPolicy.WithSplitState(settings, new PanelSplitState("2x2.rows", [0.7, 0.3])),
                     PanelLayoutPolicy.ResetSplitStates(settings)
                 })
        {
            Assert.Equal(settings.TimerSplitShortcut, transformed.TimerSplitShortcut);
            Assert.Equal(settings.TimerFinishShortcut, transformed.TimerFinishShortcut);
            Assert.Equal(settings.TimerResetShortcut, transformed.TimerResetShortcut);
        }
    }
}
