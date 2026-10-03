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

    [Fact]
    public async Task AFailedUpdateKeepsTheInstalledVersion()
    {
        var (first, firstPackage) = Build("cody.goal-tracker", "1.0.0", CommitA, "<p>one</p>");
        await InstallAsync(first, firstPackage);

        // A download that was tampered with never gets as far as a folder.
        var (second, secondPackage) = Build("cody.goal-tracker", "1.1.0", CommitB, "<p>two</p>");
        secondPackage[^1] ^= 0xFF;
        Assert.NotNull(_store.Stage(second, secondPackage, out _));

        // A swap that fails halfway puts the old version back.
        await Assert.ThrowsAnyAsync<IOException>(
            () => _store.CommitAsync(second, Path.Combine(_root, "missing"), CancellationToken.None));

        Assert.Equal("<p>one</p>", File.ReadAllText(Path.Combine(_store.FolderOf("cody.goal-tracker"), "index.html")));
        Assert.Equal([new HubInstalled("cody.goal-tracker", CommitA)], _store.LoadInstalled());
        Assert.True(_store.IsIntact("cody.goal-tracker"));
        Assert.Empty(Directory.GetDirectories(_paths.HubPluginsRoot, ".*"));
    }

    [Fact]
    public void APackageForADifferentPluginOrVersionIsRefused()
    {
        var (plugin, package) = Build("cody.goal-tracker", "1.0.0", CommitA);

        Assert.NotNull(_store.Stage(plugin with { Id = "other.plugin" }, package, out _));
        Assert.NotNull(_store.Stage(plugin with { Version = "9.9.9" }, package, out _));
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
}
