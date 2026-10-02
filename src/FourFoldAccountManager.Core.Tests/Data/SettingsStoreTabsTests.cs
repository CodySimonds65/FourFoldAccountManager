using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStoreTabsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-tab-settings-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStoreTabsTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task TabsAndTheActiveTabRoundTrip()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await _store.SaveAsync(PanelSettings.Default with
        {
            Layout = PanelLayout.Tabs,
            Tabs = [PanelTab.ForAccount(first), PanelTab.ForAccount(second)],
            ActiveTab = 1
        });
        var loaded = await _store.LoadAsync();

        Assert.Equal(PanelLayout.Tabs, loaded.Layout);
        Assert.Equal(new Guid?[] { first, second }, loaded.Tabs.Select(tab => tab.AccountId));
        Assert.Equal(1, loaded.ActiveTab);
    }

    [Fact]
    public async Task MalformedOrDuplicatedTabsNeverFailTheLoad()
    {
        // The whole value has the wrong JSON kind.
        await WriteSettingsAsync(json => json["tabs"] = "not a list");
        Assert.Empty((await _store.LoadAsync()).Tabs);

        var account = Guid.NewGuid();
        await WriteSettingsAsync(json =>
        {
            json["tabs"] = new JsonArray(
                Tab(PanelLayout.OneByOne, account.ToString()),
                // The same account again.
                Tab(PanelLayout.OneByOne, account.ToString()),
                // Not an object.
                "junk",
                // A layout a tab can't hold yet.
                Tab(PanelLayout.TwoByTwo, Guid.NewGuid().ToString()),
                // Not a GUID.
                Tab(PanelLayout.OneByOne, "not-a-guid"));
            json["activeTab"] = 9;
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(new Guid?[] { account }, loaded.Tabs.Select(tab => tab.AccountId));
        Assert.Equal(0, loaded.ActiveTab);
    }

    private static JsonObject Tab(PanelLayout layout, string accountId) => new()
    {
        ["layout"] = (int)layout,
        ["slotAccountIds"] = new JsonArray(accountId)
    };

    private async Task WriteSettingsAsync(Action<JsonObject> edit)
    {
        await _store.SaveAsync(PanelSettings.Default);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        edit(json);
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());
    }
}
