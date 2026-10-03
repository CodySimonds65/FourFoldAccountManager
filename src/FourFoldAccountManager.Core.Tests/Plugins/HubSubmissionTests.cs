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

    private string Repository(
        string id, string version, bool anySite = false, string folder = "", string name = "Goal tracker",
        string panel = "index.html", string? extraFile = null)
    {
        var root = Path.Combine(_root, "repo-" + Guid.NewGuid().ToString("N"));
        var plugin = Path.Combine(root, folder);
        Directory.CreateDirectory(plugin);
        File.WriteAllText(
            Path.Combine(plugin, "plugin.json"),
            $$"""{"id":"{{id}}","name":"{{name}}","shortLabel":"Goals","version":"{{version}}","author":"cody","apiVersion":1,"panel":"{{panel}}","anySite":{{(anySite ? "true" : "false")}}}""");
        File.WriteAllText(Path.Combine(plugin, panel), "<p>hello</p>");
        File.WriteAllText(Path.Combine(root, "README.md"), "not shipped");
        if (extraFile is not null)
        {
            File.WriteAllText(Path.Combine(plugin, extraFile), "<p>extra</p>");
        }

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
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"" + CommitA + "\",\"path\":5}")]
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"" + CommitA + "\",\"path\":null}")]
    // The same property twice reads as its last value, while a reviewer of the raw file sees the first.
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"" + CommitA + "\",\"anySite\":false,\"anySite\":true}")]
    // A lone surrogate escape: JSON the reader can't hand back as text, which once crashed the tool.
    [InlineData("cody.goal-tracker.json", "{\"repository\":\"\\ud800\",\"commit\":\"" + CommitA + "\"}")]
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

    [Fact]
    public void AnEntryPathPastTwoHundredCharactersIsRejected()
    {
        var (entry, error) = HubEntryReader.Read(
            "cody.goal-tracker.json",
            "{\"repository\":\"https://github.com/cody/goal-tracker\",\"commit\":\"" + CommitA + "\",\"path\":\"" +
            new string('a', 201) + "\"}");

        Assert.Null(entry);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[{\"id\":\"cody.goal-tracker\"}]")]
    [InlineData("[{\"id\":\"cody.goal-tracker\",\"reason\":\"a\"},{\"id\":\"cody.goal-tracker\",\"reason\":\"b\"}]")]
    [InlineData("[{\"id\":\"cody.goal-tracker\",\"reason\":\"a\",\"reason\":\"b\"}]")]
    [InlineData("[{\"id\":\"cody.goal-tracker\",\"reason\":\"\\ud800\"}]")]
    [InlineData("[{\"id\":\"\\ud800\",\"reason\":\"a\"}]")]
    public void ARemovedListThatBreaksARuleIsRejected(string json)
    {
        var (removed, error) = HubEntryReader.ReadRemoved(json);

        Assert.Null(removed);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AGoodRemovedListIsRead()
    {
        var (removed, error) = HubEntryReader.ReadRemoved("[{\"id\":\"cody.goal-tracker\",\"reason\":\"Pulled.\"}]");

        Assert.Null(error);
        Assert.Equal(new HubRemoval("cody.goal-tracker", "Pulled."), Assert.Single(removed!));
    }

    [Fact]
    public void AnEntryClearsAnyWebsiteOnlyWhenItSaysSo()
    {
        // The plugin's plugin.json asks for any website, but the maintainers' entry doesn't clear it.
        var plugin = HubSubmission.Check(
            Entry(anySite: false), Repository("cody.goal-tracker", "1.0.0", anySite: true), null, "2026-10-09").Plugin!;

        Assert.False(plugin.AnySite);
    }

    [Fact]
    public void AVersionTooLargeToCompareIsRefused()
    {
        Assert.NotNull(
            HubSubmission.Check(Entry(), Repository("cody.goal-tracker", "99999999999.0.0"), null, "2026-10-09").Error);
    }

    [Fact]
    public void APluginThatWouldNotInstallFromItsPackageIsRefused()
    {
        // The package leaves dot-files out, so this plugin would have no panel once a user installed it.
        var result = HubSubmission.Check(
            Entry(), Repository("cody.goal-tracker", "1.0.0", panel: ".index.html"), null, "2026-10-09");

        Assert.NotNull(result.Error);
        Assert.Null(result.Package);
    }

    [Fact]
    public void ARevokedClearanceIsCheckedAgainNotReused()
    {
        var cleared = HubSubmission.Check(
            Entry(anySite: true), Repository("cody.goal-tracker", "1.0.0", anySite: true), null, "2026-10-01").Plugin!;
        var current = new HubCatalog([cleared], []);
        var checkedAgain = false;

        // Same commit, but the entry no longer clears any website: the listed record must not be kept as it is.
        var build = HubSubmission.BuildCatalog(
            [Entry(anySite: false)], [], current,
            entry =>
            {
                checkedAgain = true;
                return HubSubmission.Check(
                    entry, Repository("cody.goal-tracker", "1.0.0", anySite: true), current, "2026-10-09");
            });

        Assert.True(checkedAgain);
        Assert.False(Assert.Single(build.Catalog!.Plugins).AnySite);
    }

    [Fact]
    public void AChangedRepositoryIsCheckedAgainNotReused()
    {
        var listed = HubSubmission.Check(Entry(), Repository("cody.goal-tracker", "1.0.0"), null, "2026-10-01").Plugin!;
        var moved = Entry() with { Repository = new Uri("https://github.com/someone-else/goal-tracker") };
        var checkedAgain = false;

        var build = HubSubmission.BuildCatalog(
            [moved], [], new HubCatalog([listed], []),
            entry =>
            {
                checkedAgain = true;
                return HubSubmission.Check(entry, Repository("cody.goal-tracker", "1.0.0"), null, "2026-10-09");
            });

        Assert.True(checkedAgain);
        Assert.Equal(moved.Repository, Assert.Single(build.Catalog!.Plugins).Repository);
    }

    [Fact]
    public void NoPackagesAreOfferedWhenThereIsNoCatalog()
    {
        var other = Entry() with { Id = "cody.other" };

        var build = HubSubmission.BuildCatalog(
            [Entry(), other], [], null,
            entry => entry.Id == "cody.other"
                ? new HubCheckResult("Refused.", null, null)
                : HubSubmission.Check(entry, Repository("cody.goal-tracker", "1.0.0"), null, "2026-10-09"));

        Assert.Null(build.Catalog);
        Assert.Empty(build.Packages);
        Assert.Single(build.Errors);
    }

    [Fact]
    public void ACatalogTheAppWouldRefuseIsNotBuilt()
    {
        // The app reads at most 1 MB of catalog, so a larger one would leave the hub dead for every user.
        var removals = Enumerable.Range(0, 6000)
            .Select(number => new HubRemoval($"cody.pulled-{number}", new string('x', 200)))
            .ToArray();

        var build = HubSubmission.BuildCatalog([], removals, null, _ => throw new InvalidOperationException("no entries"));

        Assert.Null(build.Catalog);
        Assert.Single(build.Errors);
    }

    [Fact]
    public void TheSummaryCannotBeSteeredByTheSubmission()
    {
        // A name that is a Markdown link, and a file name with a backtick (legal on Windows and Linux).
        var result = HubSubmission.Check(
            Entry(), Repository("cody.goal-tracker", "1.0.0", name: "[x](http://e)", extraFile: "a`b.html"), null,
            "2026-10-09");
        Assert.Null(result.Error);

        var summary = HubSubmission.Summary(
            "# [evil](http://e).json", Entry(), null, result.Plugin, result.Package, null);

        Assert.DoesNotMatch(@"(?<!\\)[\[\]`]", summary);
        Assert.DoesNotContain("[x](http://e)", summary);
        Assert.Contains(@"\[x\]\(http://e\)", summary);
        Assert.Contains("a\\`b.html", summary);
        // Every line starts with text the tool wrote, so no value can open a line of its own.
        foreach (var line in summary.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.Matches(@"^(###|\||-|\*\*|[A-Za-z])", line);
        }

        // The rejected path shows an error and the entry's name the same way.
        var rejected = HubSubmission.Summary("# [evil](http://e).json", null, "[x](http://e)\n::warning::", null, null, null);

        Assert.DoesNotContain("[x](http://e)", rejected);
        Assert.DoesNotContain("\n::", rejected);
    }

    [Fact]
    public void ARequestForAnyWebsiteIsWarnedAboutAndAnEscalationIsListed()
    {
        var before = HubSubmission.Check(Entry(), Repository("cody.goal-tracker", "1.0.0"), null, "2026-10-01").Plugin!;
        var current = new HubCatalog([before], []);
        var update = HubSubmission.Check(
            Entry(CommitB, anySite: true), Repository("cody.goal-tracker", "1.1.0", anySite: true), current, "2026-10-09");
        Assert.Null(update.Error);

        var asked = HubSubmission.Summary(
            "cody.goal-tracker.json", Entry(CommitB, anySite: true), null, update.Plugin, update.Package, current);
        var quiet = HubSubmission.Summary(
            "cody.goal-tracker.json", Entry(), null, before, update.Package, new HubCatalog([before], []));

        Assert.Contains("WARNING: this entry asks for ANY WEBSITE access", asked);
        Assert.Contains("Any-website access gained.", asked);
        Assert.DoesNotContain("WARNING", quiet);
        Assert.Contains("No change to access, sites, repository or author.", quiet);
    }
}
