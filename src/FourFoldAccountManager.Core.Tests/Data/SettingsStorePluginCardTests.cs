using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStorePluginCardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-settings-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStorePluginCardTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task PluginCardsAndDeveloperModeRoundTripThroughSaveAndLoad()
    {
        var accountId = Guid.NewGuid();
        var cards = new[]
        {
            new OverlayCardPlacement(OverlayAddOnKind.Plugin, accountId, true, new OverlayBounds(0.1, 0.2, 0.3, 0.1))
            {
                PluginCard = "cody.goal-tracker/goal"
            },
            new OverlayCardPlacement(OverlayAddOnKind.Plugin, null, false, null)
            {
                PluginCard = "cody.goal-tracker/summary"
            }
        };

        await _store.SaveAsync(PanelSettings.Default with { PluginDeveloperMode = true, OverlayCards = cards });
        var loaded = await _store.LoadAsync();

        Assert.True(loaded.PluginDeveloperMode);
        Assert.Equal(cards, loaded.OverlayCards);
    }

    [Fact]
    public async Task MalformedPluginCardsAreDroppedWithoutLosingOtherSettings()
    {
        var accountId = Guid.NewGuid();
        await _store.SaveAsync(PanelSettings.Default with { ShowOverlaysInTheatreMode = false });
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        json["overlayCards"] = new JsonArray
        {
            new JsonObject { ["kind"] = 4, ["accountId"] = null, ["enabled"] = true },
            new JsonObject { ["kind"] = 4, ["accountId"] = null, ["enabled"] = true, ["pluginCard"] = "" },
            new JsonObject { ["kind"] = 4, ["accountId"] = null, ["enabled"] = true, ["pluginCard"] = "Not Valid" },
            new JsonObject { ["kind"] = 4, ["accountId"] = null, ["enabled"] = true, ["pluginCard"] = 7 },
            new JsonObject
            {
                ["kind"] = 0, ["accountId"] = accountId.ToString(), ["enabled"] = true,
                ["pluginCard"] = "cody.goal-tracker/goal"
            },
            new JsonObject
            {
                ["kind"] = 4, ["accountId"] = null, ["enabled"] = true, ["pluginCard"] = "cody.goal-tracker/summary"
            }
        };
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());

        var loaded = await _store.LoadAsync();

        Assert.False(loaded.ShowOverlaysInTheatreMode);
        var kept = Assert.Single(loaded.OverlayCards);
        Assert.Equal("cody.goal-tracker/summary", kept.PluginCard);
    }
}
