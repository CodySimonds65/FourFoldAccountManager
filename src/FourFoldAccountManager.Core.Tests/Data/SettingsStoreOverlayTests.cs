using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStoreOverlayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-settings-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStoreOverlayTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task LegacyXpBoundsLoadAsEnabledXpCardsAndAreNotWrittenBack()
    {
        var accountId = Guid.NewGuid();
        await WriteSettingsAsync(accountId, json =>
        {
            json.Remove("overlayCards");
            json["xpOverlayBoundsByAccount"] = new JsonObject
            {
                [accountId.ToString()] = new JsonObject { ["x"] = 0.1, ["y"] = 0.2, ["width"] = 0.3, ["height"] = 0.1 }
            };
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(
            [new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, new OverlayBounds(0.1, 0.2, 0.3, 0.1))],
            loaded.OverlayCards);
        Assert.Null(loaded.LegacyXpOverlayBoundsByAccount);
        await _store.SaveAsync(loaded);
        Assert.DoesNotContain("xpOverlayBoundsByAccount", await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(loaded.OverlayCards, (await _store.LoadAsync()).OverlayCards);
    }

    [Fact]
    public async Task OverlayCardsRoundTripThroughSaveAndLoad()
    {
        var accountId = Guid.NewGuid();
        var cards = new[]
        {
            new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, false, new OverlayBounds(0.5, 0.5, 0.2, 0.1))
        };

        await _store.SaveAsync(PanelSettings.Default with
        {
            SlotAccountIds = [accountId, null, null, null, null],
            OverlayCards = cards
        });

        Assert.Equal(cards, (await _store.LoadAsync()).OverlayCards);
    }

    [Fact]
    public async Task UnknownKindsAndScopeMismatchesAreDroppedWithoutFailingTheLoad()
    {
        var accountId = Guid.NewGuid();
        await WriteSettingsAsync(accountId, json => json["overlayCards"] = new JsonArray
        {
            new JsonObject { ["kind"] = 99, ["accountId"] = accountId.ToString(), ["enabled"] = true },
            new JsonObject { ["kind"] = 0, ["accountId"] = null, ["enabled"] = true },
            new JsonObject
            {
                ["kind"] = 0, ["accountId"] = accountId.ToString(), ["enabled"] = true,
                ["bounds"] = new JsonObject { ["x"] = 0.9, ["y"] = 0, ["width"] = 0.5, ["height"] = 0.5 }
            }
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal([new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, null)], loaded.OverlayCards);
    }

    [Fact]
    public async Task OverlayCardsAsAnObjectLoadsAsEmptyInsteadOfThrowing()
    {
        await WriteSettingsAsync(Guid.NewGuid(), json => json["overlayCards"] = new JsonObject());

        var loaded = await _store.LoadAsync();

        Assert.Empty(loaded.OverlayCards);
    }

    [Fact]
    public async Task SecondMonitorSettingsRoundTripThroughSaveAndLoad()
    {
        var accountId = Guid.NewGuid();
        var cards = new[]
        {
            new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, new OverlayBounds(0.5, 0.5, 0.2, 0.1))
            {
                IsFloating = true,
                // A second monitor to the left of the main one has a negative Left.
                FloatingBounds = new FloatingCardBounds(-1500, 120, 200, 52)
            }
        };
        var window = new ToolsWindowPlacement(true, -1500, 120, 320, 700, true);

        await _store.SaveAsync(PanelSettings.Default with
        {
            SlotAccountIds = [accountId, null, null, null, null],
            OverlayCards = cards,
            ToolsWindow = window,
            SecondMonitorMode = SecondMonitorMode.FloatingCards
        });
        var loaded = await _store.LoadAsync();

        Assert.Equal(cards, loaded.OverlayCards);
        Assert.Equal(window, loaded.ToolsWindow);
        Assert.Equal(SecondMonitorMode.FloatingCards, loaded.SecondMonitorMode);
    }

    [Fact]
    public async Task MalformedSecondMonitorSettingsLoadWithoutFailingTheLoad()
    {
        // Wrong JSON kind.
        await WriteSettingsAsync(Guid.NewGuid(), json => json["toolsWindow"] = "not an object");
        Assert.Null((await _store.LoadAsync()).ToolsWindow);

        // A field of the wrong JSON kind.
        await WriteSettingsAsync(Guid.NewGuid(), json => json["toolsWindow"] = new JsonObject { ["width"] = "wide" });
        Assert.Null((await _store.LoadAsync()).ToolsWindow);

        // Smaller than the tools window's minimum size.
        await WriteSettingsAsync(Guid.NewGuid(), json => json["toolsWindow"] = new JsonObject
        {
            ["isOpen"] = true, ["left"] = 0, ["top"] = 0, ["width"] = 100, ["height"] = 100, ["isMaximized"] = false
        });
        Assert.Null((await _store.LoadAsync()).ToolsWindow);

        // A mode this build doesn't know.
        await WriteSettingsAsync(Guid.NewGuid(), json => json["secondMonitorMode"] = 9);
        Assert.Equal(SecondMonitorMode.AccountToolsWindow, (await _store.LoadAsync()).SecondMonitorMode);

        // Unusable floating bounds drop the bounds, not the card.
        var accountId = Guid.NewGuid();
        await WriteSettingsAsync(accountId, json => json["overlayCards"] = new JsonArray
        {
            new JsonObject
            {
                ["kind"] = 0, ["accountId"] = accountId.ToString(), ["enabled"] = true, ["isFloating"] = true,
                ["floatingBounds"] = new JsonObject { ["left"] = 0, ["top"] = 0, ["width"] = 0, ["height"] = 52 }
            }
        });
        var card = Assert.Single((await _store.LoadAsync()).OverlayCards);
        Assert.True(card.IsFloating);
        Assert.Null(card.FloatingBounds);
    }

    private async Task WriteSettingsAsync(Guid accountId, Action<JsonObject> edit)
    {
        await _store.SaveAsync(PanelSettings.Default with { SlotAccountIds = [accountId, null, null, null, null] });
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        edit(json);
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());
    }
}
