using System.Buffers.Binary;
using System.Text;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Plugins;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class PluginManifestReaderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"fourfold-plugin-{Guid.NewGuid():N}");
    private readonly string _outsideFile;
    private readonly string _evilFolder;

    public PluginManifestReaderTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "index.html"), "<!doctype html>");
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        File.WriteAllText(Path.Combine(_folder, "sub", "index.html"), "<!doctype html>");

        var parentFolder = Path.GetDirectoryName(_folder)!;
        var pluginFolderName = Path.GetFileName(_folder);
        _outsideFile = Path.Combine(parentFolder, $"outside-{Guid.NewGuid():N}.html");
        _evilFolder = Path.Combine(parentFolder, $"{pluginFolderName}-evil");

        File.WriteAllText(_outsideFile, "<!doctype html>");
        Directory.CreateDirectory(_evilFolder);
        File.WriteAllText(Path.Combine(_evilFolder, "index.html"), "<!doctype html>");
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        if (File.Exists(_outsideFile))
        {
            File.Delete(_outsideFile);
        }
        if (Directory.Exists(_evilFolder))
        {
            Directory.Delete(_evilFolder, recursive: true);
        }
    }

    private PluginManifestResult Read(string json)
    {
        File.WriteAllText(Path.Combine(_folder, "plugin.json"), json);
        return PluginManifestReader.Read(_folder);
    }

    private PluginManifestResult ReadWithIcon(byte[] iconBytes)
    {
        File.WriteAllBytes(Path.Combine(_folder, "icon.png"), iconBytes);
        return Read(Manifest(", \"icon\": \"icon.png\""));
    }

    // The first 24 bytes of a PNG: the signature, the IHDR chunk's length and name, then its width and height.
    private static byte[] PngHeader(uint width, uint height)
    {
        var header = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 13);
        "IHDR"u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), height);
        return header;
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
    [InlineData("\"id\": \"xn.foo\"")]
    [InlineData("\"id\": \"nul.tools\"")]
    [InlineData("\"id\": \"com1.tools\"")]
    [InlineData("\"id\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"")]
    [InlineData("\"apiVersion\": 4")]
    [InlineData("\"apiVersion\": \"1\"")]
    [InlineData("\"panel\": \"missing.html\"")]
    [InlineData("\"panel\": \"plugin.json\"")]
    [InlineData("\"panel\": \"sub\\\\index.html\"")]
    [InlineData("\"shortLabel\": \"Far too long\"")]
    [InlineData("\"version\": \"1.0\"")]
    [InlineData("\"sites\": [\"http://wiki.example.com\"]")]
    [InlineData("\"sites\": [\"https://wiki.example.com/path\"]")]
    [InlineData("\"sites\": [\"https://192.168.1.10\"]")]
    [InlineData("\"sites\": [\"https://localhost\"]")]
    [InlineData("\"sites\": [\"https://app.localhost\"]")]
    [InlineData("\"sites\": [\"https://localhost.\"]")]
    [InlineData("\"sites\": [\"https://127.0.0.1.\"]")]
    [InlineData("\"sites\": [\"https://app.localhost.\"]")]
    [InlineData("\"sites\": [\"https://x.fourfoldplugin\"]")]
    [InlineData("\"sites\": [\"https://printer.local\"]")]
    [InlineData("\"sites\": [\"https://nas.lan\"]")]
    [InlineData("\"sites\": [\"https://router\"]")]
    // The JSON escape \u0007 (a bell character) is what the file holds, so it is the reader's control-character rule
    // that rejects these, not the JSON parser.
    [InlineData("\"name\": \"Goal\\u0007tracker\"")]
    [InlineData("\"cards\": [{ \"id\": \"goal\", \"name\": \"Go\\u0007al\", \"scope\": \"account\" }]")]
    // U+202E is a right-to-left override, which would make FourFold draw the rest of the name backwards.
    [InlineData("\"name\": \"Goal\\u202Etracker\"")]
    // A zero-width joiner in a host makes Uri.IdnHost throw rather than return a name.
    [InlineData("\"sites\": [\"https://a\u200Db.example\"]")]
    // Uri's conversion turns look-alike characters into punctuation (U+FE64 into "<", U+FF5C into "|"), and "_" isn't a
    // host name character, so a site must be plain letters, digits, dots and dashes once converted.
    [InlineData("\"sites\": [\"https://a\uFE64b.example.com\"]")]
    [InlineData("\"sites\": [\"https://x\uFF5Cy.example.com\"]")]
    [InlineData("\"sites\": [\"https://a_b.example.com\"]")]
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

    [Theory]
    [InlineData("https://b\u00FCcher.example", "xn--bcher-kva.example")]
    [InlineData("https://xn--bcher-kva.example", "xn--bcher-kva.example")]
    [InlineData("https://Wiki.Example.com:8443", "wiki.example.com")]
    public void ASiteThatIsAPlainHostNameIsAccepted(string site, string host)
    {
        var result = Read(Manifest(", \"sites\": [\"" + site + "\"]"));

        Assert.Null(result.Error);
        Assert.Equal(host, Assert.Single(result.Manifest!.Sites).IdnHost);
    }

    [Fact]
    public void AMissingOrBrokenManifestIsRejectedWithAReason()
    {
        Assert.Equal("plugin.json is missing.", PluginManifestReader.Read(_folder).Error);
        Assert.Equal("plugin.json isn't valid JSON.", Read("{ not json").Error);
    }

    [Fact]
    public void AnOversizedManifestIsRejectedWithoutBeingRead()
    {
        var result = Read(Manifest() + new string(' ', 70 * 1024));

        Assert.Null(result.Manifest);
        Assert.Equal("plugin.json is too large.", result.Error);
    }

    [Fact]
    public void AnIconThatDeclaresAHugeImageIsRejectedWithoutBeingDecoded()
    {
        // A 274-byte PNG declaring 1 by 100000 pixels made the app commit 215 MB the moment it drew the strip.
        var result = ReadWithIcon(PngHeader(width: 1, height: 100000));

        Assert.Null(result.Manifest);
        Assert.Contains("icon", result.Error);
        Assert.Null(ReadWithIcon(PngHeader(width: 100000, height: 1)).Manifest);
    }

    [Fact]
    public void AnIconThatIsNotAPngIsRejectedWhateverItIsNamed()
    {
        var result = ReadWithIcon(Encoding.ASCII.GetBytes("This is text, not a PNG image."));

        Assert.Null(result.Manifest);
        Assert.Contains("icon", result.Error);
    }

    [Fact]
    public void AnIconWithASmallPngHeaderIsAccepted()
    {
        var result = ReadWithIcon(PngHeader(width: 64, height: 64));

        Assert.Null(result.Error);
        Assert.Equal("icon.png", result.Manifest!.Icon);
    }

    [Fact]
    public void AManifestReferencingAnOutsideFileIsRejected()
    {
        var relativePath = Path.Combine("..", Path.GetFileName(_outsideFile));
        var result = Read(Manifest($", \"panel\": \"{relativePath.Replace("\\", "/")}\""));

        Assert.Null(result.Manifest);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void AManifestReferencingASiblingFolderIsRejected()
    {
        var folderName = Path.GetFileName(_evilFolder);
        var result = Read(Manifest($", \"panel\": \"../{folderName}/index.html\""));

        Assert.Null(result.Manifest);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
