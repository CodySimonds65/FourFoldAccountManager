namespace FourFoldAccountManager.Core.Plugins;

public static class PluginPaths
{
    // Resolves relativePath inside folder. False when the path is rooted, empty, or escapes the folder, so a plugin
    // can never name a file outside its own folder.
    public static bool TryResolveInside(string folder, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains('\0') || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }
}
