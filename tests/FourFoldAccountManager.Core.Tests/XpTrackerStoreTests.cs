using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Tests;

public class XpTrackerStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    private static XpStoredAccount Entry(Guid id) => new(id, 83, Now,
        new PlayerProgressSnapshot("Desmond", "Scout", new Dictionary<string, ClassXpSnapshot>
        {
            ["Scout"] = new(13, 900, 910, null)
        }, []), [new XpGainInterval(Now.AddMinutes(-2), Now, 50)]);

    [Fact]
    public async Task Saves_loads_and_removes_accounts_independently()
    {
        var root = Path.Combine(Path.GetTempPath(), "fourfold-xp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new XpTrackerStore(new LocalDataPaths(root));
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            await store.SaveAsync([Entry(first), Entry(second)], Now);
            var loaded = await store.LoadAsync();
            Assert.Equal(2, loaded.Count);
            Assert.Equal(900, loaded[first].Snapshot.Classes["Scout"].CurrentXp);
            await store.RemoveAsync(first);
            loaded = await store.LoadAsync();
            Assert.False(loaded.ContainsKey(first));
            Assert.True(loaded.ContainsKey(second));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Malformed_tracker_data_is_ignored_without_touching_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "fourfold-xp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "xp-tracker.json");
            await File.WriteAllTextAsync(path, "{broken");
            Assert.Empty(await new XpTrackerStore(new LocalDataPaths(root)).LoadAsync());
            Assert.Equal("{broken", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
