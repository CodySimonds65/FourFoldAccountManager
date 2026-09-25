using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStorePluginsSidebarTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-sidebar-settings-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStorePluginsSidebarTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task CollapsedPluginsSidebarSurvivesARestart()
    {
        await _store.SaveAsync(PanelSettings.Default with { PluginsSidebarExpanded = false });

        Assert.False((await _store.LoadAsync()).PluginsSidebarExpanded);
    }

    [Fact]
    public async Task SettingsWrittenBeforeTheToggleExistedLoadExpanded()
    {
        await _store.SaveAsync(PanelSettings.Default with { PluginsSidebarExpanded = false });
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        json.Remove("pluginsSidebarExpanded");
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());

        Assert.True((await _store.LoadAsync()).PluginsSidebarExpanded);
    }
}
