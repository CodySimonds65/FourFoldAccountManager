using FourFoldAccountManager.Core.Plugins.Hub;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class HubSubmissionTests : IDisposable
{
    private const string CommitA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string CommitB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string GoodEntry =
        "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"" + CommitA + "\"}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fourfold-submit-" + Guid.NewGuid().ToString("N"));

    public HubSubmissionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Repository(string id, string version, bool anySite = false, string folder = "")
    {
        var root = Path.Combine(_root, "repo-" + Guid.NewGuid().ToString("N"));
        var plugin = Path.Combine(root, folder);
        Directory.CreateDirectory(plugin);
        File.WriteAllText(
            Path.Combine(plugin, "plugin.json"),
            $$"""{"id":"{{id}}","name":"Goal tracker","shortLabel":"Goals","version":"{{version}}","author":"cody","apiVersion":1,"panel":"index.html","anySite":{{(anySite ? "true" : "false")}}}""");
        File.WriteAllText(Path.Combine(plugin, "index.html"), "<p>hello</p>");
        File.WriteAllText(Path.Combine(root, "README.md"), "not shipped");
        return root;
    }

    private static HubEntry Entry(string commit = CommitA, string path = "", bool anySite = false) =>
        new("cody.goal-tracker", new Uri("https://github.com/cody/goal-tracker"), commit, path, anySite);

    [Theory]
    [InlineData("Not An Id.json", GoodEntry)]
    [InlineData("cody.goal-tracker.txt", GoodEntry)]
    [InlineData("cody.goal-tracker.json", "not json")]
    [InlineData("cody.goal-tracker.json", "[]")]
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://gitlab.com/cody/goal-tracker\",\"commit\":\"" + CommitA + "\"}")]
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"main\"}")]
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"aaaaaaa\"}")]
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"" + CommitA + "\",\"path\":\"../other\"}")]
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"" + CommitA + "\",\"path\":\"/abs\"}")]
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"" + CommitA + "\",\"anySite\":\"yes\"}")]
    public void AnEntryThatBreaksARuleIsRejected(string fileName, string json)
    {
        var (entry, error) = HubEntryReader.Read(fileName, json);

        Assert.Null(entry);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AGoodEntryIsRead()
    {
        var (entry, error) = HubEntryReader.Read(
            "cody.goal-tracker.json",
            "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"" + CommitA +
            "\",\"path\":\"samples/goal-tracker\",\"anySite\":true}");

        Assert.Null(error);
        Assert.Equal(Entry(path: "samples/goal-tracker", anySite: true), entry);
    }

    [Fact]
    public void ASubmissionMustMatchItsEntry()
    {
        // The plugin.json names another plugin than the file is named for.
        Assert.NotNull(HubSubmission.Check(Entry(), Repository("other.plugin", "1.0.0"), null, "2026-10-09").Error);
        // The entry clears any website, but the plugin never asked for it.
        Assert.NotNull(
            HubSubmission.Check(Entry(anySite: true), Repository("cody.goal-tracker", "1.0.0"), null, "2026-10-09").Error);
        // The folder the entry names isn't in the repository.
        Assert.NotNull(
            HubSubmission.Check(Entry(path: "plugin"), Repository("cody.goal-tracker", "1.0.0"), null, "2026-10-09").Error);

        var good = HubSubmission.Check(
            Entry(path: "plugin", anySite: true), Repository("cody.goal-tracker", "1.0.0", anySite: true, folder: "plugin"),
            null, "2026-10-09");

        Assert.Null(good.Error);
        Assert.True(good.Plugin!.AnySite);
        Assert.Equal("2026-10-09", good.Plugin.Reviewed);
        Assert.True(PluginPackage.Matches(good.Package!, good.Plugin.Size, good.Plugin.Sha256));
    }

    [Fact]
    public void AnUpdateMustRaiseTheVersion()
    {
        var listed = HubSubmission.Check(Entry(), Repository("cody.goal-tracker", "1.2.0"), null, "2026-10-01").Plugin!;
        var current = new HubCatalog([listed], []);

        Assert.NotNull(
            HubSubmission.Check(Entry(CommitB), Repository("cody.goal-tracker", "1.2.0"), current, "2026-10-09").Error);
        Assert.NotNull(
            HubSubmission.Check(Entry(CommitB), Repository("cody.goal-tracker", "1.1.9"), current, "2026-10-09").Error);
        Assert.Null(
            HubSubmission.Check(Entry(CommitB), Repository("cody.goal-tracker", "1.10.0"), current, "2026-10-09").Error);
    }

    [Fact]
    public void AnUnchangedEntryKeepsItsRecordWithoutBeingFetchedAgain()
    {
        var listed = HubSubmission.Check(Entry(), Repository("cody.goal-tracker", "1.0.0"), null, "2026-10-01").Plugin!;

        // The author's repository may be gone by now; an unchanged entry must not need it.
        var build = HubSubmission.BuildCatalog(
            [Entry()], [], new HubCatalog([listed], []), _ => throw new InvalidOperationException("fetched again"));

        Assert.Empty(build.Errors);
        Assert.Empty(build.Packages);
        Assert.Same(listed, Assert.Single(build.Catalog!.Plugins));
    }

    [Fact]
    public void APluginCannotBeBothListedAndRemoved()
    {
        var build = HubSubmission.BuildCatalog(
            [Entry()], [new HubRemoval("cody.goal-tracker", "Pulled.")], null,
            entry => HubSubmission.Check(entry, Repository("cody.goal-tracker", "1.0.0"), null, "2026-10-09"));

        Assert.Null(build.Catalog);
        Assert.Single(build.Errors);
    }
}
