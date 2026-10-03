using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Plugins.Hub;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class HubStoreTests : IDisposable
{
    private const string CommitA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string CommitB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fourfold-hub-" + Guid.NewGuid().ToString("N"));
    private readonly LocalDataPaths _paths;
    private readonly HubStore _store;

    public HubStoreTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new LocalDataPaths(Path.Combine(_root, "data"));
        _store = new HubStore(_paths);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // A real package for a small plugin, and the catalog record that names it.
    private (HubPlugin Plugin, byte[] Package) Build(string id, string version, string commit, string page = "<p>v</p>")
    {
        var source = Path.Combine(_root, "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        File.WriteAllText(
            Path.Combine(source, "plugin.json"),
            $$"""{"id":"{{id}}","name":"Goal tracker","shortLabel":"Goals","version":"{{version}}","author":"cody","apiVersion":1,"panel":"index.html"}""");
        File.WriteAllText(Path.Combine(source, "index.html"), page);
        var package = PluginPackage.Build(source);
        return (
            new HubPlugin(
                id, "Goal tracker", "Goals", version, "cody", "", [], false, [],
                new Uri("https://github.com/cody/goal-tracker"), commit, "2026-10-09", package.Length,
                PluginPackage.Sha256(package)),
            package);
    }

    private async Task InstallAsync(HubPlugin plugin, byte[] package)
    {
        Assert.Null(_store.Stage(plugin, package, out var staging));
        await _store.CommitAsync(plugin, staging, CancellationToken.None);
    }

    private string[] DotFolders() =>
        Directory.GetDirectories(_paths.HubPluginsRoot, ".*").Select(folder => Path.GetFileName(folder)).ToArray();

    [Fact]
    public async Task AFailedUpdateKeepsTheInstalledVersion()
    {
        var (first, firstPackage) = Build("cody.goal-tracker", "1.0.0", CommitA, "<p>one</p>");
        await InstallAsync(first, firstPackage);

        // An intact package still has to be the one the catalog records, by hash and by size.
        var (second, secondPackage) = Build("cody.goal-tracker", "1.1.0", CommitB, "<p>two</p>");
        Assert.NotNull(_store.Stage(second with { Sha256 = new string('0', 64) }, secondPackage, out _));
        Assert.NotNull(_store.Stage(second with { Size = second.Size + 1 }, secondPackage, out _));

        // A download that was tampered with never gets as far as a folder.
        secondPackage[^1] ^= 0xFF;
        Assert.NotNull(_store.Stage(second, secondPackage, out _));

        // A swap that fails halfway puts the old version back.
        await Assert.ThrowsAnyAsync<IOException>(
            () => _store.CommitAsync(second, Path.Combine(_root, "missing"), CancellationToken.None));

        Assert.Equal("<p>one</p>", File.ReadAllText(Path.Combine(_store.FolderOf("cody.goal-tracker"), "index.html")));
        Assert.Equal([new HubInstalled("cody.goal-tracker", CommitA)], _store.LoadInstalled());
        Assert.True(_store.IsIntact("cody.goal-tracker"));
        Assert.Empty(DotFolders());
    }

    [Fact]
    public async Task AFailedRecordSaveKeepsTheInstalledVersion()
    {
        var (first, firstPackage) = Build("cody.goal-tracker", "1.0.0", CommitA, "<p>one</p>");
        await InstallAsync(first, firstPackage);
        var (second, secondPackage) = Build("cody.goal-tracker", "1.1.0", CommitB, "<p>two</p>");
        Assert.Null(_store.Stage(second, secondPackage, out var staging));

        // Readable, so the record loads, but it can't be swapped: the save fails after the folders were exchanged.
        using (new FileStream(_paths.HubInstalledFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(
                () => _store.CommitAsync(second, staging, CancellationToken.None));
        }

        Assert.Equal("<p>one</p>", File.ReadAllText(Path.Combine(_store.FolderOf("cody.goal-tracker"), "index.html")));
        Assert.Equal([new HubInstalled("cody.goal-tracker", CommitA)], _store.LoadInstalled());
        Assert.True(_store.IsIntact("cody.goal-tracker"));
        Assert.Empty(DotFolders());
    }

    [Fact]
    public void APackageForADifferentPluginOrVersionIsRefused()
    {
        var (plugin, package) = Build("cody.goal-tracker", "1.0.0", CommitA);

        Assert.NotNull(_store.Stage(plugin with { Id = "other.plugin" }, package, out _));
        Assert.NotNull(_store.Stage(plugin with { Version = "9.9.9" }, package, out _));

        // The sites the hub page showed, and any-website clearance, must match what the package's plugin.json asks for.
        Assert.NotNull(_store.Stage(plugin with { Sites = [new Uri("https://example.org")] }, package, out _));
        Assert.NotNull(_store.Stage(plugin with { AnySite = true }, package, out _));
        Assert.Empty(Directory.GetDirectories(_paths.HubPluginsRoot));

        Assert.Null(_store.Stage(plugin, package, out var staging));
        Assert.True(File.Exists(Path.Combine(staging, "plugin.json")));
    }

    [Fact]
    public async Task UninstallRemovesTheFilesTheSavedSettingsAndTheRecord()
    {
        var (goal, goalPackage) = Build("cody.goal-tracker", "1.0.0", CommitA);
        var (other, otherPackage) = Build("other.plugin", "1.0.0", CommitA);
        await InstallAsync(goal, goalPackage);
        await InstallAsync(other, otherPackage);
        Directory.CreateDirectory(_paths.PluginDataRoot);
        var saved = Path.Combine(_paths.PluginDataRoot, "cody.goal-tracker.json");
        File.WriteAllText(saved, "{}");

        await _store.UninstallAsync("cody.goal-tracker", CancellationToken.None);

        Assert.False(Directory.Exists(_store.FolderOf("cody.goal-tracker")));
        Assert.False(File.Exists(saved));
        Assert.Equal([new HubInstalled("other.plugin", CommitA)], _store.LoadInstalled());
        Assert.True(_store.IsIntact("other.plugin"));
        Assert.Empty(DotFolders());
    }

    [Fact]
    public async Task ALockedRecordIsNotReadAsNothingInstalled()
    {
        var (plugin, package) = Build("cody.goal-tracker", "1.0.0", CommitA);
        await InstallAsync(plugin, package);

        using (new FileStream(_paths.HubInstalledFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Reading it as empty would make the next install write a record that forgets this plugin.
            Assert.ThrowsAny<IOException>(() => _store.LoadInstalled());
        }

        Assert.Equal([new HubInstalled("cody.goal-tracker", CommitA)], _store.LoadInstalled());
    }

    [Fact]
    public async Task ACorruptRecordIsRebuiltFromTheInstalledFolders()
    {
        var (first, firstPackage) = Build("cody.goal-tracker", "1.0.0", CommitA);
        var (second, secondPackage) = Build("other.plugin", "1.0.0", CommitA);
        var (third, thirdPackage) = Build("third.plugin", "1.0.0", CommitB);
        await InstallAsync(first, firstPackage);
        await InstallAsync(second, secondPackage);
        Assert.True(HubCatalogJson.IsCommit(HubStore.UnknownCommit));

        // Leftovers and a folder that isn't a whole plugin are not mistaken for installed plugins.
        Directory.CreateDirectory(Path.Combine(_paths.HubPluginsRoot, ".staging-x"));
        Directory.CreateDirectory(Path.Combine(_paths.HubPluginsRoot, "broken.plugin"));

        foreach (var damaged in new[] { "{ not json", "", "null", """[{"id":5}]""" })
        {
            File.WriteAllText(_paths.HubInstalledFilePath, damaged);
            Assert.Equal(
                [new HubInstalled("cody.goal-tracker", HubStore.UnknownCommit), new HubInstalled("other.plugin", HubStore.UnknownCommit)],
                _store.LoadInstalled());
        }

        // The next install keeps the others instead of writing a record that forgets them.
        File.WriteAllText(_paths.HubInstalledFilePath, "{ not json");
        await InstallAsync(third, thirdPackage);

        Assert.Equal(
            [
                new HubInstalled("cody.goal-tracker", HubStore.UnknownCommit),
                new HubInstalled("other.plugin", HubStore.UnknownCommit),
                new HubInstalled("third.plugin", CommitB)
            ],
            _store.LoadInstalled());

        // A missing record is still just "nothing installed", not a reason to guess from the folders.
        File.Delete(_paths.HubInstalledFilePath);
        Assert.Empty(_store.LoadInstalled());
    }

    [Fact]
    public async Task InstallsAtTheSameTimeAreAllRecorded()
    {
        var staged = new List<(HubPlugin Plugin, string Staging)>();
        for (var number = 1; number <= 8; number++)
        {
            var (plugin, package) = Build($"cody.plugin-{number}", "1.0.0", CommitA);
            Assert.Null(_store.Stage(plugin, package, out var staging));
            staged.Add((plugin, staging));
        }

        await Task.WhenAll(staged.Select(item =>
            Task.Run(() => _store.CommitAsync(item.Plugin, item.Staging, CancellationToken.None))));

        Assert.Equal(
            staged.Select(item => item.Plugin.Id).Order(StringComparer.Ordinal),
            _store.LoadInstalled().Select(record => record.Id).Order(StringComparer.Ordinal));
        Assert.All(staged, item => Assert.True(_store.IsIntact(item.Plugin.Id)));
    }

    [Fact]
    public async Task CleanUpRemovesOnlyLeftovers()
    {
        var (plugin, package) = Build("cody.goal-tracker", "1.0.0", CommitA);
        await InstallAsync(plugin, package);
        foreach (var leftover in new[] { ".staging-x", ".trash-y" })
        {
            Directory.CreateDirectory(Path.Combine(_paths.HubPluginsRoot, leftover));
            File.WriteAllText(Path.Combine(_paths.HubPluginsRoot, leftover, "file.txt"), "x");
        }

        // A set-aside copy of a plugin that still has its folder is only a leftover.
        var setAside = Path.Combine(_paths.HubPluginsRoot, ".old-cody.goal-tracker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(setAside);

        _store.CleanUp();

        Assert.Empty(DotFolders());
        Assert.True(_store.IsIntact("cody.goal-tracker"));
        Assert.Equal([new HubInstalled("cody.goal-tracker", CommitA)], _store.LoadInstalled());
    }

    [Fact]
    public async Task CleanUpPutsBackAPluginAfterACrashMidSwap()
    {
        var (plugin, package) = Build("cody.goal-tracker", "1.0.0", CommitA, "<p>one</p>");
        await InstallAsync(plugin, package);

        // The state a crash leaves between the two moves of an update: the old folder set aside, the new one not in place.
        Directory.Move(
            _store.FolderOf("cody.goal-tracker"),
            Path.Combine(_paths.HubPluginsRoot, ".old-cody.goal-tracker-" + Guid.NewGuid().ToString("N")));
        Assert.False(_store.IsIntact("cody.goal-tracker"));

        _store.CleanUp();

        Assert.True(_store.IsIntact("cody.goal-tracker"));
        Assert.Equal("<p>one</p>", File.ReadAllText(Path.Combine(_store.FolderOf("cody.goal-tracker"), "index.html")));
        Assert.Empty(DotFolders());
    }

    [Fact]
    public async Task TheSavedCatalogIsReadBack()
    {
        var plugin = new HubPlugin(
            "cody.goal-tracker", "Goal tracker", "Goals", "1.0.0", "cody", "", [], false, [],
            new Uri("https://github.com/cody/goal-tracker"), CommitA, "2026-10-09", 100, new string('a', 64));
        var catalog = new HubCatalog([plugin], [new HubRemoval("pulled.plugin", "Sent data away.")]);
        Assert.Null(_store.LoadCatalog());

        await _store.SaveCatalogAsync(HubCatalogJson.Write(catalog), CancellationToken.None);

        var loaded = _store.LoadCatalog();
        Assert.NotNull(loaded);
        var listed = Assert.Single(loaded.Plugins);
        Assert.Equal((plugin.Id, plugin.Commit, plugin.Sha256), (listed.Id, listed.Commit, listed.Sha256));
        Assert.Equal(new HubRemoval("pulled.plugin", "Sent data away."), Assert.Single(loaded.Removed));
    }

    [Fact]
    public async Task AnIdThatIsNotAPluginIdNeverReachesTheDisk()
    {
        var (plugin, package) = Build("cody.goal-tracker", "1.0.0", CommitA);
        await InstallAsync(plugin, package);
        var victim = Path.Combine(_paths.DataRoot, "victim");
        Directory.CreateDirectory(victim);
        File.WriteAllText(Path.Combine(victim, "keep.txt"), "x");

        Assert.Throws<ArgumentException>(() => _store.FolderOf("../victim"));
        Assert.Throws<ArgumentException>(() => _store.FolderOf("..\\victim"));
        Assert.Throws<ArgumentException>(() => _store.IsIntact("../victim"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.UninstallAsync("../victim", CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(victim, "keep.txt")));
        Assert.Equal([new HubInstalled("cody.goal-tracker", CommitA)], _store.LoadInstalled());
        Assert.True(_store.IsIntact("cody.goal-tracker"));
    }
}
