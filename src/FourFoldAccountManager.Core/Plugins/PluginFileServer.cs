namespace FourFoldAccountManager.Core.Plugins;

// Maps a request path on a plugin's origin to a file in its folder. Only known static file types are served, and
// never plugin.json or anything outside the folder.
public static class PluginFileServer
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
        [".woff2"] = "font/woff2"
    };

    // Whether a file of this name is a type FourFold serves; the hub packages only these.
    public static bool IsServed(string path) => ContentTypes.ContainsKey(Path.GetExtension(path));

    public static bool TryResolve(PluginManifest manifest, string urlPath, out string filePath, out string contentType)
    {
        filePath = string.Empty;
        contentType = string.Empty;
        if (urlPath is null)
        {
            return false;
        }

        string relative;
        try
        {
            relative = Uri.UnescapeDataString(urlPath).TrimStart('/');
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (relative.Length == 0)
        {
            relative = manifest.Panel;
        }

        // A backslash is a path separator on Windows, so "..\" would climb just like "../".
        if (relative.Contains('\\') || relative.Contains(':') ||
            !PluginPaths.TryResolveInside(manifest.Folder, relative, out var resolved) ||
            !ContentTypes.TryGetValue(Path.GetExtension(resolved), out var type) ||
            Path.GetFileName(resolved).Equals("plugin.json", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(resolved))
        {
            return false;
        }

        filePath = resolved;
        contentType = type;
        return true;
    }
}
