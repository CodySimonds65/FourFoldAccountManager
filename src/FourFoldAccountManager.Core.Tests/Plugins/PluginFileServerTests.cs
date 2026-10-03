using FourFoldAccountManager.Core.Plugins;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class PluginFileServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-serve-{Guid.NewGuid():N}");
    private readonly PluginManifest _manifest;

    public PluginFileServerTests()
    {
        var folder = Path.Combine(_root, "plugin");
        Directory.CreateDirectory(Path.Combine(folder, "js"));
        File.WriteAllText(Path.Combine(folder, "index.html"), "<!doctype html>");
        File.WriteAllText(Path.Combine(folder, "js", "app.js"), "");
        File.WriteAllText(Path.Combine(folder, "run.exe"), "");
        File.WriteAllText(Path.Combine(folder, "plugin.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "secret.txt"), "outside the plugin");
        _manifest = new PluginManifest("cody.goal-tracker", "Goal tracker", "Goals", "1.0.0", "Cody", "", 1,
            "index.html", null, [], false, []) { Folder = folder };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("/", "index.html", "text/html; charset=utf-8")]
    [InlineData("/index.html", "index.html", "text/html; charset=utf-8")]
    [InlineData("/js/app.js", "app.js", "text/javascript; charset=utf-8")]
    public void FilesInsideThePluginFolderAreServedWithAFixedContentType(string urlPath, string fileName, string type)
    {
        Assert.True(PluginFileServer.TryResolve(_manifest, urlPath, out var filePath, out var contentType));
        Assert.Equal(fileName, Path.GetFileName(filePath));
        Assert.Equal(type, contentType);
    }

    [Theory]
    [InlineData("/../secret.txt")]
    [InlineData("/%2e%2e/secret.txt")]
    [InlineData("/js/../../secret.txt")]
    [InlineData("/..%5csecret.txt")]
    [InlineData("/C:/Windows/win.ini")]
    [InlineData("/run.exe")]
    [InlineData("/missing.html")]
    [InlineData("/plugin.json")]
    [InlineData("/PLUGIN.JSON")]
    [InlineData("/js/../plugin.json")]
    [InlineData("/index.html:x")]
    [InlineData("/index.html:x.js")]
    [InlineData("/index.html::$DATA")]
    public void PathsOutsideTheFolderUnknownTypesAndMissingFilesAreRefused(string urlPath) =>
        Assert.False(PluginFileServer.TryResolve(_manifest, urlPath, out _, out _));
}
