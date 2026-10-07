using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStoreLiveFeedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-live-feed-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStoreLiveFeedTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // Every settings file written before this feature lacks the switch; the feed is on by default.
    [Fact]
    public async Task OlderSettingsWithoutTheSwitchLoadWithTheFeedOn()
    {
        await _store.SaveAsync(PanelSettings.Default);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        json.Remove("liveGameFeed");
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());

        Assert.True((await _store.LoadAsync()).LiveGameFeed);
    }

    [Fact]
    public async Task TurningTheFeedOffSurvivesARestart()
    {
        await _store.SaveAsync(PanelSettings.Default with { LiveGameFeed = false });

        Assert.False((await _store.LoadAsync()).LiveGameFeed);
    }
}
