using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStoreXpTargetTests : IDisposable
{
    private static readonly Guid Alice = Guid.NewGuid();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-xp-targets-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStoreXpTargetTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task TargetsSurviveARestart()
    {
        await _store.SaveAsync(PanelSettings.Default with
        {
            XpCalculatorTargetLevels = new Dictionary<Guid, long> { [Alice] = 50 }
        });

        Assert.Equal(new Dictionary<Guid, long> { [Alice] = 50 }, (await _store.LoadAsync()).XpCalculatorTargetLevels);
    }

    [Fact]
    public async Task BadTargetDataStillLoads()
    {
        var bob = Guid.NewGuid();
        await WriteSettingsAsync(json => json["xpCalculatorTargetLevels"] = new JsonObject
        {
            [Alice.ToString()] = 50,
            ["not-a-guid"] = 5,
            [bob.ToString()] = -3,
            [Guid.NewGuid().ToString()] = "x",
            [Guid.Empty.ToString()] = 7
        });

        Assert.Equal(new Dictionary<Guid, long> { [Alice] = 50 }, (await _store.LoadAsync()).XpCalculatorTargetLevels);

        foreach (JsonNode? wrongShape in new JsonNode?[] { JsonValue.Create("oops"), new JsonArray(1, 2), null })
        {
            await WriteSettingsAsync(json => json["xpCalculatorTargetLevels"] = wrongShape?.DeepClone());
            Assert.Empty((await _store.LoadAsync()).XpCalculatorTargetLevels);
        }
    }

    [Fact]
    public async Task SettingsWrittenBeforeTargetsExistedLoadWithNone()
    {
        await WriteSettingsAsync(json => json.Remove("xpCalculatorTargetLevels"));

        Assert.Empty((await _store.LoadAsync()).XpCalculatorTargetLevels);
    }

    private async Task WriteSettingsAsync(Action<JsonObject> edit)
    {
        await _store.SaveAsync(PanelSettings.Default);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        edit(json);
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());
    }
}
