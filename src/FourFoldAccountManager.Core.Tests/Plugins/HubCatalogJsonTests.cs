using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Core.Plugins.Hub;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class HubCatalogJsonTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly string Hash = new('a', 64);

    private static HubPlugin Plugin(string id = "cody.goal-tracker") => new(
        id, "Goal tracker", "Goals", "1.2.0", "cody", "Tracks a level goal.",
        [new Uri("https://example.com")], false, [new HubCard("Goal", OverlayAddOnScope.Account)],
        new Uri("https://github.com/cody/goal-tracker"), Commit, "2026-10-09", 18234, Hash);

    [Fact]
    public void AWrittenCatalogReadsBackTheSame()
    {
        var written = HubCatalogJson.Write(new HubCatalog([Plugin()], [new HubRemoval("x.y", "Broken.")]));

        var read = HubCatalogJson.Parse(written)!;

        var plugin = Assert.Single(read.Plugins);
        var expected = Plugin();
        Assert.Equal(expected.Id, plugin.Id);
        Assert.Equal(expected.Name, plugin.Name);
        Assert.Equal(expected.ShortLabel, plugin.ShortLabel);
        Assert.Equal(expected.Version, plugin.Version);
        Assert.Equal(expected.Author, plugin.Author);
        Assert.Equal(expected.Description, plugin.Description);
        Assert.Equal(expected.Sites, plugin.Sites);
        Assert.Equal(expected.AnySite, plugin.AnySite);
        Assert.Equal(expected.Cards, plugin.Cards);
        Assert.Equal(expected.Repository, plugin.Repository);
        Assert.Equal(expected.Commit, plugin.Commit);
        Assert.Equal(expected.Reviewed, plugin.Reviewed);
        Assert.Equal(expected.Size, plugin.Size);
        Assert.Equal(expected.Sha256, plugin.Sha256);
        Assert.Equal([new HubRemoval("x.y", "Broken.")], read.Removed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"plugins\":[]}")]
    [InlineData("{\"version\":\"1\"}")]
    [InlineData("{\"version\":1,\"plugins\":\"x\"}")]
    [InlineData("{\"version\":1,\"removed\":{}}")]
    [InlineData("{\"version\":1,\"removed\":\"x\",\"plugins\":[]}")]
    public void ACatalogThatCannotBeUsedReadsAsNothing(string? json) => Assert.Null(HubCatalogJson.Parse(json));

    [Fact]
    public void AMissingOrNullListReadsAsEmpty()
    {
        var read = HubCatalogJson.Parse("{\"version\":1,\"plugins\":null}")!;

        Assert.Empty(read.Plugins);
        Assert.Empty(read.Removed);
    }

    [Fact]
    public void ARawLoneSurrogateReadsAsNothingNotAnException() =>
        Assert.Null(HubCatalogJson.Parse("{\"version\":1,\"pad\":\"\ud800\"}"));

    [Fact]
    public void AnEscapedLoneSurrogateCostsOnlyItsOwnItem()
    {
        var read = HubCatalogJson.Parse(
            "{\"version\":1,\"plugins\":[]," +
            "\"removed\":[{\"id\":\"pulled.plugin\",\"reason\":\"\\ud800\"}," +
            "{\"id\":\"\\ud800\",\"reason\":\"x\"}," +
            "{\"id\":\"other.plugin\",\"reason\":\"Gone.\"}]}")!;

        Assert.Equal([new HubRemoval("pulled.plugin", string.Empty), new HubRemoval("other.plugin", "Gone.")], read.Removed);
    }

    [Fact]
    public void ACatalogThatStartsWithAByteOrderMarkStillReads() =>
        Assert.Single(HubCatalogJson.Parse("\uFEFF" + HubCatalogJson.Write(new HubCatalog([Plugin()], [])))!.Plugins);

    [Fact]
    public void ASecondEntryWithTheSameIdIsDropped()
    {
        var catalog = JsonNode.Parse(HubCatalogJson.Write(new HubCatalog([Plugin()], [])))!;
        var second = catalog["plugins"]![0]!.DeepClone();
        second["commit"] = new string('b', 40);
        catalog["plugins"]!.AsArray().Add(second);

        var read = HubCatalogJson.Parse(catalog.ToJsonString())!;

        Assert.Equal(Commit, Assert.Single(read.Plugins).Commit);
    }

    [Fact]
    public void AnAddressIsNeverBuiltFromAnUncheckedIdCommitOrRepository()
    {
        var repository = new Uri("https://github.com/cody/goal-tracker");

        Assert.Throws<ArgumentException>(() => HubAddresses.PackageName("../evil", Commit));
        Assert.Throws<ArgumentException>(() => HubAddresses.Package("cody.goal-tracker", "main"));
        Assert.Throws<ArgumentException>(() => HubAddresses.RepositoryArchive(repository, "main"));
        Assert.Throws<ArgumentException>(
            () => HubAddresses.RepositoryArchive(new Uri("https://evil.example/cody/goal-tracker"), Commit));
        Assert.Throws<ArgumentException>(
            () => HubAddresses.RepositoryArchive(new Uri("https://github.com/cody/goal-tracker/tree/main"), Commit));
        Assert.EndsWith($"/cody.goal-tracker-{Commit}.zip", HubAddresses.Package("cody.goal-tracker", Commit).AbsoluteUri);
        Assert.Equal(
            $"https://codeload.github.com/cody/goal-tracker/zip/{Commit}",
            HubAddresses.RepositoryArchive(repository, Commit).AbsoluteUri);
    }

    [Fact]
    public void AVersionWithATrailingNewlineIsNotAVersion() =>
        Assert.False(PluginManifestReader.IsVersion("1.2.0\n"));

    [Fact]
    public void AnOversizedCatalogReadsAsNothing() =>
        Assert.Null(HubCatalogJson.Parse(
            "{\"version\":1,\"pad\":\"" + new string('x', HubLimits.MaximumCatalogBytes) + "\"}"));

    [Theory]
    [InlineData("id", "\"../evil\"")]
    [InlineData("id", "\"nul.tools\"")]
    [InlineData("id", "7")]
    [InlineData("commit", "\"abc\"")]
    [InlineData("commit", "\"0123456789ABCDEF0123456789ABCDEF01234567\"")]
    [InlineData("commit", "\"../../0123456789abcdef0123456789abcdef0123\"")]
    [InlineData("sha256", "\"zz\"")]
    [InlineData("size", "0")]
    [InlineData("size", "99999999")]
    [InlineData("size", "\"1\"")]
    [InlineData("repository", "\"https://evil.example/cody/goal-tracker\"")]
    [InlineData("repository", "\"https://github.com/cody/goal-tracker/tree/main\"")]
    [InlineData("name", "\"Goal\\u202Etracker\"")]
    [InlineData("name", "\"\"")]
    [InlineData("version", "\"1.2\"")]
    [InlineData("sites", "[\"http://example.com\"]")]
    [InlineData("sites", "[\"https://localhost\"]")]
    [InlineData("sites", "[\"https://a\\u200Db.example\"]")]
    [InlineData("cards", "[{\"name\":\"Goal\",\"scope\":\"everywhere\"}]")]
    public void ABadEntryIsSkippedAndTheOthersLoad(string property, string value)
    {
        var catalog = JsonNode.Parse(HubCatalogJson.Write(new HubCatalog([Plugin("good.plugin")], [])))!;
        var bad = JsonNode.Parse(HubCatalogJson.Write(new HubCatalog([Plugin("bad.plugin")], [])))!["plugins"]![0]!
            .DeepClone();
        bad[property] = JsonNode.Parse(value);
        catalog["plugins"]!.AsArray().Insert(0, bad);

        var read = HubCatalogJson.Parse(catalog.ToJsonString());

        Assert.Equal("good.plugin", Assert.Single(read!.Plugins).Id);
    }

    [Fact]
    public void APulledPluginIsNeverAlsoListedAndAPullSurvivesABadReason()
    {
        var catalog = JsonNode.Parse(
            HubCatalogJson.Write(new HubCatalog([Plugin("pulled.plugin"), Plugin("kept.plugin")], [])))!;
        catalog["removed"] = JsonNode.Parse(
            "[{\"id\":\"pulled.plugin\",\"reason\":\"Bad\\u202Ereason\"},{\"id\":\"../x\",\"reason\":\"ignored\"}]");

        var read = HubCatalogJson.Parse(catalog.ToJsonString())!;

        Assert.Equal("kept.plugin", Assert.Single(read.Plugins).Id);
        Assert.Equal([new HubRemoval("pulled.plugin", string.Empty)], read.Removed);
    }
}
