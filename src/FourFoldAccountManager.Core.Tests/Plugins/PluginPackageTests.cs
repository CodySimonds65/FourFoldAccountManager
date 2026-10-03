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
