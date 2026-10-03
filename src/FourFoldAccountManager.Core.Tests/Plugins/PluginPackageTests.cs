using System.IO.Compression;
using FourFoldAccountManager.Core.Plugins.Hub;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class PluginPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fourfold-package-" + Guid.NewGuid().ToString("N"));

    public PluginPackageTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static byte[] Zip(params (string Name, byte[] Content)[] entries) => Zip(null, entries);

    private static byte[] Zip(Action<ZipArchiveEntry>? configure, params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                configure?.Invoke(entry);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    [Fact]
    public void APackageThatDoesNotMatchTheCatalogIsRefused()
    {
        var package = Zip(("plugin.json", "{}"u8.ToArray()));
        var hash = PluginPackage.Sha256(package);

        Assert.True(PluginPackage.Matches(package, package.Length, hash));
        Assert.False(PluginPackage.Matches(package, package.Length + 1, hash));
        Assert.False(PluginPackage.Matches(package, package.Length, new string('0', 64)));
        Assert.False(PluginPackage.Matches([.. package, 0], package.Length, hash));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("/evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("./evil.txt")]
    public void AnEntryThatEscapesTheFolderIsRefused(string name)
    {
        var target = Path.Combine(_root, "inner", "plugin");

        Assert.Throws<InvalidDataException>(
            () => PluginPackage.Extract(Zip((name, new byte[] { 1 })), target));

        Assert.Empty(Directory.EnumerateFiles(_root, "evil.txt", SearchOption.AllDirectories));
    }

    [Fact]
    public void TooManyFilesOrTooMuchDataIsRefused()
    {
        var many = Enumerable.Range(0, HubLimits.MaximumPackageFiles + 1)
            .Select(index => ($"file{index}.txt", new byte[] { 1 }))
            .ToArray();
        Assert.Throws<InvalidDataException>(() => PluginPackage.Extract(Zip(many), Path.Combine(_root, "many")));

        // Zeros compress to almost nothing, so the package is small and only the unpacked size gives it away.
        var large = Zip(("big.txt", new byte[HubLimits.MaximumUnpackedBytes + 1]));
        Assert.True(large.Length < 100_000);
        Assert.Throws<InvalidDataException>(() => PluginPackage.Extract(large, Path.Combine(_root, "large")));

        // Each entry is under the limit; only the total is over it.
        var two = Zip(("a.txt", new byte[3 * 1024 * 1024]), ("b.txt", new byte[3 * 1024 * 1024]));
        Assert.Throws<InvalidDataException>(() => PluginPackage.Extract(two, Path.Combine(_root, "two")));
    }

    [Fact]
    public void AnEntryNestedTooDeeplyIsRefused()
    {
        // Deleting a tree thousands of levels deep overflows the stack, so a deep entry must never be unpacked.
        var name = string.Join('/', Enumerable.Repeat("a", 100)) + "/f.txt";
        var target = Path.Combine(_root, "deep");

        Assert.Throws<InvalidDataException>(() => PluginPackage.Extract(Zip((name, new byte[] { 1 })), target));

        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public void AFileWhereAFolderAlreadyIsIsRefused()
    {
        using var package = new MemoryStream(Zip(("a/b.txt", new byte[] { 1 }), ("a", new byte[] { 1 })));

        var exception = Assert.ThrowsAny<IOException>(
            () => SafeArchive.Extract(
                package, Path.Combine(_root, "clash"), 500, HubLimits.MaximumUnpackedBytes, stripTopFolder: false));

        Assert.IsNotType<UnauthorizedAccessException>(exception);
    }

    [Fact]
    public void AFolderThatAlreadyHasFilesIsNotUnpackedInto()
    {
        var target = Path.Combine(_root, "taken");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "mine.txt"), "mine");

        Assert.Throws<IOException>(() => PluginPackage.Extract(Zip(("plugin.json", "{}"u8.ToArray())), target));

        Assert.Equal(["mine.txt"], Directory.EnumerateFileSystemEntries(target).Select(path => Path.GetFileName(path)));
    }

    [Fact]
    public void LinksAndOddNamesAreSkippedOnlyWhenAsked()
    {
        var package = Zip(
            entry =>
            {
                if (entry.FullName == "link.txt")
                {
                    entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                }
            },
            ("link.txt", "target"u8.ToArray()),
            ("a:b.txt", new byte[] { 1 }),
            ("logs\\c.log", new byte[] { 1 }),
            ("ok.txt", new byte[] { 1 }));
        var skipped = Path.Combine(_root, "skipped");

        using (var stream = new MemoryStream(package))
        {
            SafeArchive.Extract(
                stream, skipped, 500, HubLimits.MaximumUnpackedBytes, stripTopFolder: false, skipLinksAndOddNames: true);
        }

        Assert.Equal(["ok.txt"], Directory.EnumerateFileSystemEntries(skipped).Select(path => Path.GetFileName(path)));

        using var strict = new MemoryStream(package);
        Assert.Throws<InvalidDataException>(
            () => SafeArchive.Extract(
                strict, Path.Combine(_root, "strict"), 500, HubLimits.MaximumUnpackedBytes, stripTopFolder: false));
    }

    [Fact]
    public void APackageWithAFileFourFoldDoesNotServeIsRefusedBeforeAnythingIsWritten()
    {
        var package = Zip(("plugin.json", "{}"u8.ToArray()), ("run.exe", new byte[] { 1 }));
        var target = Path.Combine(_root, "exe");

        Assert.Throws<InvalidDataException>(() => PluginPackage.Extract(package, target));

        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void FileNamesWindowsCannotUnpackAreFoundWhenBuilding()
    {
        Assert.Null(PluginPackage.UnpackableReason(["plugin.json", "index.html", "img/a.png", "img/b.png"]));
        Assert.NotNull(PluginPackage.UnpackableReason(["a:b.png"]));
        Assert.NotNull(PluginPackage.UnpackableReason(["what?.png"]));
        Assert.NotNull(PluginPackage.UnpackableReason(["trail.png."]));
        Assert.NotNull(PluginPackage.UnpackableReason(["dir /a.png"]));
        Assert.NotNull(PluginPackage.UnpackableReason(["A.png", "a.png"]));
        Assert.NotNull(PluginPackage.UnpackableReason(["aux.js"]));
        Assert.NotNull(PluginPackage.UnpackableReason(["nul"]));
        // Only the device names themselves, not names that start with the same letters.
        Assert.Null(PluginPackage.UnpackableReason(["console.js", "auxiliary.png", "com10.png"]));
    }

    [Fact]
    public void ALinkInAnArchiveIsRefused()
    {
        // A Unix symbolic link: file type 0xA in the mode bits a zip keeps in the high half of this field.
        var package = Zip(
            entry => entry.ExternalAttributes = unchecked((int)0xA1FF0000), ("link.txt", "target"u8.ToArray()));

        Assert.Throws<InvalidDataException>(() => PluginPackage.Extract(package, Path.Combine(_root, "link")));
    }

    [Fact]
    public void BuildLeavesOutFilesFourFoldDoesNotServeAndIsRepeatable()
    {
        var plugin = Path.Combine(_root, "plugin");
        Directory.CreateDirectory(Path.Combine(plugin, ".github"));
        Directory.CreateDirectory(Path.Combine(plugin, "img"));
        foreach (var name in new[]
                 {
                     "plugin.json", "index.html", "README.md", ".gitignore", "tool.exe", ".github/ci.yml", "img/a.png"
                 })
        {
            File.WriteAllText(Path.Combine(plugin, name), name);
        }

        var first = PluginPackage.Build(plugin);
        var second = PluginPackage.Build(plugin);

        using var archive = new ZipArchive(new MemoryStream(first));
        Assert.Equal(["img/a.png", "index.html", "plugin.json"], archive.Entries.Select(entry => entry.FullName));
        Assert.Equal(first, second);
    }
}
