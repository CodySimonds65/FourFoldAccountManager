using System.Text.Json;
using FourFoldAccountManager.Core.Plugins;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class PluginStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-storage-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_root, "cody.goal-tracker.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task ValuesSurviveANewInstanceReadingTheSameFile()
    {
        await new PluginStorage(FilePath).SetAsync("goals", Json("""{"level":50}"""));

        var loaded = await new PluginStorage(FilePath).GetAsync("goals");

        Assert.Equal(50, loaded!.Value.GetProperty("level").GetInt32());
        Assert.Null(await new PluginStorage(FilePath).GetAsync("missing"));
    }

    [Fact]
    public async Task AWriteOverTheSizeCapIsRejectedAndKeepsExistingData()
    {
        var storage = new PluginStorage(FilePath);
        await storage.SetAsync("kept", Json("\"safe\""));
        var tooBig = Json("\"" + new string('x', PluginStorage.MaximumBytes) + "\"");

        var error = await Assert.ThrowsAsync<PluginApiException>(() => storage.SetAsync("big", tooBig));

        Assert.Equal("limit-exceeded", error.Code);
        Assert.Equal("safe", (await new PluginStorage(FilePath).GetAsync("kept"))!.Value.GetString());
        Assert.Null(await new PluginStorage(FilePath).GetAsync("big"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("this-key-is-longer-than-the-sixty-four-characters-a-plugin-key-may-be")]
    public async Task ABadKeyIsRejected(string key)
    {
        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => new PluginStorage(FilePath).SetAsync(key, Json("1")));

        Assert.Equal("invalid-argument", error.Code);
    }

    [Fact]
    public async Task AnUnreadableStoreLoadsAsEmptyAndCanBeWrittenAgain()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(FilePath, "{ not json");
        var storage = new PluginStorage(FilePath);

        Assert.Null(await storage.GetAsync("goals"));
        await storage.SetAsync("goals", Json("1"));
        Assert.Equal(1, (await new PluginStorage(FilePath).GetAsync("goals"))!.Value.GetInt32());
    }

    [Fact]
    public async Task RemoveDeletesOnlyThatKey()
    {
        var storage = new PluginStorage(FilePath);
        await storage.SetAsync("a", Json("1"));
        await storage.SetAsync("b", Json("2"));

        await storage.RemoveAsync("a");

        Assert.Null(await new PluginStorage(FilePath).GetAsync("a"));
        Assert.Equal(2, (await new PluginStorage(FilePath).GetAsync("b"))!.Value.GetInt32());
    }
}
