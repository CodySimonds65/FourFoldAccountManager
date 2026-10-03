using System.Text;
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

    [Fact]
    public async Task AValueThatOnlyFitsWhenCompactIsRejected()
    {
        // A JSON array of 40,000 ones is ~80 KB compact (under cap) but ~320 KB indented (over cap).
        // This test proves the size check measures indented JSON, not compact.
        var json = "[" + string.Join(",", Enumerable.Repeat("1", 40000)) + "]";
        var compactBytes = Encoding.UTF8.GetByteCount(json);
        Assert.True(compactBytes < PluginStorage.MaximumBytes,
            $"Compact size {compactBytes} must be under {PluginStorage.MaximumBytes} to test the boundary");

        var storage = new PluginStorage(FilePath);
        await storage.SetAsync("kept", Json("\"safe\""));
        var largeArray = Json(json);

        var error = await Assert.ThrowsAsync<PluginApiException>(() => storage.SetAsync("list", largeArray));

        Assert.Equal("limit-exceeded", error.Code);
        Assert.Equal("safe", (await new PluginStorage(FilePath).GetAsync("kept"))!.Value.GetString());
        Assert.Null(await new PluginStorage(FilePath).GetAsync("list"));
    }

    [Fact]
    public async Task AnAllowedWriteStaysWithinTheCapOnDisk()
    {
        // A JSON array of 20,000 ones is ~160 KB indented (under cap).
        var json = "[" + string.Join(",", Enumerable.Repeat("1", 20000)) + "]";
        var storage = new PluginStorage(FilePath);

        var largeArray = Json(json);
        await storage.SetAsync("list", largeArray);

        // Verify the file size is within the cap
        Assert.True(new FileInfo(FilePath).Length <= PluginStorage.MaximumBytes);

        // Verify the data persists and reads back correctly
        var loaded = await new PluginStorage(FilePath).GetAsync("list");
        Assert.NotNull(loaded);
        var array = loaded.Value.EnumerateArray().ToList();
        Assert.Equal(20000, array.Count);
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

    [Fact]
    public async Task ALockedStoreIsNotTreatedAsEmptyAndKeepsItsData()
    {
        // Write key "a" with one instance
        var storage1 = new PluginStorage(FilePath);
        await storage1.SetAsync("a", Json("1"));

        // Lock the file to simulate a transient read failure
        FileStream? lockStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            // With a NEW PluginStorage instance, GetAsync should throw "unavailable"
            var storage2 = new PluginStorage(FilePath);
            var error = await Assert.ThrowsAsync<PluginApiException>(() => storage2.GetAsync("a"));
            Assert.Equal("unavailable", error.Code);

            // Dispose the lock
            lockStream.Dispose();
            lockStream = null;

            // With that same instance, SetAsync should now succeed
            await storage2.SetAsync("b", Json("2"));

            // A fresh instance should read both "a" and "b"
            var storage3 = new PluginStorage(FilePath);
            Assert.Equal(1, (await storage3.GetAsync("a"))!.Value.GetInt32());
            Assert.Equal(2, (await storage3.GetAsync("b"))!.Value.GetInt32());
        }
        finally
        {
            lockStream?.Dispose();
        }
    }
}
