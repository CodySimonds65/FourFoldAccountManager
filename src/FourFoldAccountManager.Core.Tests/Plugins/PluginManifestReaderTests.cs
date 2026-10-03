using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Plugins;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class PluginManifestReaderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"fourfold-plugin-{Guid.NewGuid():N}");

    public PluginManifestReaderTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "index.html"), "<!doctype html>");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private PluginManifestResult Read(string json)
    {
        File.WriteAllText(Path.Combine(_folder, "plugin.json"), json);
        return PluginManifestReader.Read(_folder);
    }

    private static string Manifest(string overrides = "") => $$"""
        {
          "id": "cody.goal-tracker", "name": "Goal tracker", "shortLabel": "Goals", "version": "1.0.0",
          "author": "Cody", "apiVersion": 1, "panel": "index.html",
          "sites": ["https://wiki.example.com"],
          "cards": [{ "id": "goal", "name": "Goal", "scope": "account" }]
          {{overrides}}
        }
        """;

    [Fact]
    public void AValidManifestLoadsWithItsSitesAndCards()
    {
        var result = Read(Manifest());

        Assert.Null(result.Error);
        var manifest = Assert.IsType<PluginManifest>(result.Manifest);
        Assert.Equal("cody.goal-tracker", manifest.Id);
        Assert.Equal([new Uri("https://wiki.example.com")], manifest.Sites);
        Assert.False(manifest.AnySite);
        Assert.Equal([new PluginCardManifest("goal", "Goal", OverlayAddOnScope.Account)], manifest.Cards);
        Assert.Equal(_folder, manifest.Folder);
    }

    [Theory]
    [InlineData("\"id\": \"goaltracker\"")]
    [InlineData("\"id\": \"Cody.Goal\"")]
    [InlineData("\"id\": \"cody..goal\"")]
    [InlineData("\"id\": \"timer\"")]
    [InlineData("\"apiVersion\": 2")]
    [InlineData("\"panel\": \"../outside.html\"")]
    [InlineData("\"panel\": \"missing.html\"")]
    [InlineData("\"panel\": \"plugin.json\"")]
    [InlineData("\"shortLabel\": \"Far too long\"")]
    [InlineData("\"version\": \"1.0\"")]
    [InlineData("\"sites\": [\"http://wiki.example.com\"]")]
    [InlineData("\"sites\": [\"https://wiki.example.com/path\"]")]
    [InlineData("\"sites\": [\"https://192.168.1.10\"]")]
    [InlineData("\"sites\": [\"https://localhost\"]")]
    [InlineData("\"sites\": [\"https://app.localhost\"]")]
    [InlineData("\"cards\": [{ \"id\": \"Goal\", \"name\": \"Goal\", \"scope\": \"account\" }]")]
    [InlineData("\"cards\": [{ \"id\": \"goal\", \"name\": \"Goal\", \"scope\": \"panel\" }]")]
    [InlineData("\"cards\": [{ \"id\": \"a\", \"name\": \"A\", \"scope\": \"global\" }, { \"id\": \"a\", \"name\": \"B\", \"scope\": \"global\" }]")]
    public void AManifestThatBreaksARuleIsRejectedWithAReason(string overridingField)
    {
        // A later duplicate key wins in System.Text.Json, so appending the field overrides the valid value.
        var result = Read(Manifest("," + overridingField));

        Assert.Null(result.Manifest);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void AMissingOrBrokenManifestIsRejectedWithAReason()
    {
        Assert.Equal("plugin.json is missing.", PluginManifestReader.Read(_folder).Error);
        Assert.Equal("plugin.json isn't valid JSON.", Read("{ not json").Error);
    }
}
