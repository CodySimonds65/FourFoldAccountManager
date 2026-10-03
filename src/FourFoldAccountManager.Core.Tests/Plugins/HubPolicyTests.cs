using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Core.Plugins.Hub;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class HubPolicyTests
{
    private const string CommitA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string CommitB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static HubPlugin Listed(string id, string commit, bool anySite = false) => new(
        id, "Name", "Short", "1.0.0", "author", "", [], anySite, [],
        new Uri("https://github.com/author/repo"), commit, "2026-10-09", 100, new string('a', 64));

    [Fact]
    public void APulledOrUnlistedPluginNeverLoads()
    {
        var catalog = new HubCatalog(
            [Listed("kept.plugin", CommitA)],
            [new HubRemoval("pulled.plugin", "Sent data away."), new HubRemoval("quiet.plugin", "")]);
        HubInstalled[] installed =
        [
            new("kept.plugin", CommitA), new("pulled.plugin", CommitA), new("quiet.plugin", CommitA),
            new("gone.plugin", CommitA)
        ];

        var loads = HubPolicy.Loads(catalog, installed, id => id);

        Assert.Equal("kept.plugin", Assert.Single(loads).Id);
        Assert.Null(HubPolicy.PulledReason(catalog, "kept.plugin"));
        Assert.Equal("Sent data away.", HubPolicy.PulledReason(catalog, "pulled.plugin"));
        Assert.Equal(HubPolicy.RemovedReason, HubPolicy.PulledReason(catalog, "quiet.plugin"));
        Assert.Equal(HubPolicy.UnlistedReason, HubPolicy.PulledReason(catalog, "gone.plugin"));
    }

    [Fact]
    public void AnyWebsiteNeedsTheCatalogsClearanceForTheInstalledCommit()
    {
        var catalog = new HubCatalog(
            [
                Listed("cleared.plugin", CommitA, anySite: true), Listed("plain.plugin", CommitA),
                Listed("updating.plugin", CommitB, anySite: true)
            ],
            []);
        HubInstalled[] installed =
            [new("cleared.plugin", CommitA), new("plain.plugin", CommitA), new("updating.plugin", CommitA)];

        // The update that was cleared isn't installed yet, so the old code doesn't get the clearance early.
        Assert.Equal(
            [PluginTrust.Verified, PluginTrust.Standard, PluginTrust.Standard],
            HubPolicy.Loads(catalog, installed, id => id).Select(load => load.Trust));
        Assert.Equal("updating.plugin", Assert.Single(HubPolicy.Updates(catalog, installed)).Id);

        // No catalog at all (a first start offline): everything installed still runs, with its declared sites only.
        var offline = HubPolicy.Loads(null, installed, id => id);
        Assert.Equal(3, offline.Count);
        Assert.All(offline, load => Assert.Equal(PluginTrust.Standard, load.Trust));
        Assert.Empty(HubPolicy.Updates(null, installed));
    }

    [Fact]
    public void ADevPluginReplacesTheHubPluginOfTheSameId()
    {
        HubLoad[] loads =
        [
            new("cody.goal-tracker", "a", CommitA, PluginTrust.Standard),
            new("other.plugin", "b", CommitA, PluginTrust.Standard)
        ];

        Assert.Equal(
            "other.plugin",
            Assert.Single(HubPolicy.Visible(loads, new HashSet<string> { "cody.goal-tracker" })).Id);
        Assert.Equal(2, HubPolicy.Visible(loads, new HashSet<string>()).Count);
    }

    [Fact]
    public void UninstallingRemovesThePluginsCardsAndIdsFromSettings()
    {
        var accountId = Guid.NewGuid();
        var settings = PanelSettings.Default with
        {
            PluginOrder = ["xp-tracker", "cody.goal-tracker", "other.plugin"],
            DisabledPlugins = ["cody.goal-tracker"],
            OpenPlugin = "cody.goal-tracker",
            OverlayCards =
            [
                new OverlayCardPlacement(OverlayAddOnKind.Plugin, accountId, true, null)
                {
                    PluginCard = "cody.goal-tracker/goal"
                },
                // Another plugin whose id merely starts the same way keeps its card.
                new OverlayCardPlacement(OverlayAddOnKind.Plugin, null, true, null)
                {
                    PluginCard = "cody.goal-tracker-two/goal"
                },
                new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, null)
            ]
        };

        var next = HubPolicy.WithUninstalled(settings, "cody.goal-tracker");

        Assert.Equal(["xp-tracker", "other.plugin"], next.PluginOrder);
        Assert.Empty(next.DisabledPlugins);
        Assert.Null(next.OpenPlugin);
        Assert.Equal(
            [null, "cody.goal-tracker-two/goal"],
            next.OverlayCards.Select(card => card.PluginCard).OrderBy(card => card));
        Assert.Same(next, HubPolicy.WithUninstalled(next, "cody.goal-tracker"));
    }
}
