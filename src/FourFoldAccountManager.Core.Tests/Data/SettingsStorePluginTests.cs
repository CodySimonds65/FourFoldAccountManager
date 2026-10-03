using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStorePluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-settings-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStorePluginTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task PluginLayoutRoundTripsThroughSaveAndLoad()
    {
        await _store.SaveAsync(PanelSettings.Default with
        {
            PluginOrder = ["timer", "xp-tracker", "someone.community-plugin"],
            DisabledPlugins = ["stats"],
            OpenPlugin = "timer",
            PluginsSidebarExpanded = false,
            PluginStripVisible = false
        });

        var loaded = await _store.LoadAsync();

        Assert.False(loaded.PluginStripVisible);
        Assert.Equal(["timer", "xp-tracker", "someone.community-plugin"], loaded.PluginOrder);
        Assert.Equal(["stats"], loaded.DisabledPlugins);
        Assert.Equal("timer", loaded.OpenPlugin);
        Assert.False(loaded.PluginsSidebarExpanded);
    }

    [Fact]
    public async Task MalformedPluginValuesLoadAsDefaultsWithoutLosingOtherSettings()
    {
        await WriteSettingsAsync(json =>
        {
            json["showOverlaysInTheatreMode"] = false;
            json["pluginOrder"] = "stats";
            json["disabledPlugins"] = new JsonArray { 1, null, " ", "timer", "timer" };
            json["openPlugin"] = 5;
        });

        var loaded = await _store.LoadAsync();

        Assert.False(loaded.ShowOverlaysInTheatreMode);
        Assert.Empty(loaded.PluginOrder);
        Assert.Equal(["timer"], loaded.DisabledPlugins);
        Assert.Equal("xp-tracker", loaded.OpenPlugin);
    }

    [Fact]
    public async Task OlderSettingsWithoutPluginFieldsKeepTheirHiddenSidebar()
    {
        await WriteSettingsAsync(json =>
        {
            json.Remove("pluginOrder");
            json.Remove("disabledPlugins");
            json.Remove("openPlugin");
            json["pluginsSidebarExpanded"] = false;
        });

        var loaded = await _store.LoadAsync();

        Assert.False(loaded.PluginsSidebarExpanded);
        Assert.Empty(loaded.PluginOrder);
        Assert.Empty(loaded.DisabledPlugins);
        Assert.Equal("xp-tracker", loaded.OpenPlugin);
    }

    private async Task WriteSettingsAsync(Action<JsonObject> edit)
    {
        await _store.SaveAsync(PanelSettings.Default);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        edit(json);
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());
    }
}
