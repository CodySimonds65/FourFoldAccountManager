using System.IO.Compression;

namespace FourFoldAccountManager.Core.Plugins.Hub;

// Unpacks a zip that came from the network. Anything that could write outside the target folder, or fill the disk,
// throws InvalidDataException before it is written. The caller deletes the target folder when this throws.
public static class SafeArchive
{
    // Deleting a folder tree is recursive and overflows the stack when the tree is thousands of levels deep, which
    // nothing can catch, so no entry may nest deeper than this.
    private const int MaximumDepth = 64;

    // stripTopFolder drops each entry's first path part, for GitHub's repository zips, which wrap everything in one
    // "<repo>-<commit>" folder. skipLinksAndOddNames skips links and names with a ':' or '\' instead of refusing the
    // archive, for an author's whole repository, where such a file has nothing to do with the plugin.
    public static void Extract(
        Stream zip, string targetFolder, int maximumFiles, long maximumBytes, bool stripTopFolder,
        bool skipLinksAndOddNames = false)
    {
        // Callers unpack into a fresh folder; one with files in it could hold a link that leads somewhere else.
        if (Directory.Exists(targetFolder) && Directory.EnumerateFileSystemEntries(targetFolder).Any())
        {
            throw new IOException("The folder to unpack into isn't empty.");
        }

        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        Directory.CreateDirectory(targetFolder);
        var files = 0;
        long bytes = 0;
        var buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.EndsWith('/'))
            {
                continue;
            }

            var parts = name.Split('/');
            if (parts.Length > MaximumDepth)
            {
                throw new InvalidDataException("The archive nests folders too deeply.");
            }

            if (parts.Any(part => part.Length == 0 || part is "." or ".."))
            {
                throw new InvalidDataException("The archive contains a path that isn't allowed.");
            }

            // A Unix symbolic link is stored as a file whose mode bits say "link".
            var isLink = ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
            if (isLink || name.Contains('\\') || name.Contains(':'))
            {
                if (skipLinksAndOddNames)
                {
                    continue;
                }

                throw new InvalidDataException(
                    isLink ? "The archive contains a link." : "The archive contains a path that isn't allowed.");
            }

            if (stripTopFolder)
            {
                if (parts.Length == 1)
                {
                    continue;
                }

                parts = parts[1..];
            }

            if (!PluginPaths.TryResolveInside(targetFolder, string.Join('/', parts), out var path))
            {
                throw new InvalidDataException("The archive contains a path that isn't allowed.");
            }

            if (++files > maximumFiles)
            {
                throw new InvalidDataException("The archive has too many files.");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var input = entry.Open();
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    // Counted as it is written: the size an entry declares can lie.
                    bytes += read;
                    if (bytes > maximumBytes)
                    {
                        throw new InvalidDataException("The archive is too large.");
                    }

                    output.Write(buffer, 0, read);
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                // A file where a folder is needed, or the reverse; callers expect only InvalidDataException or
                // IOException from this method.
                throw new IOException("The archive can't be unpacked: access to a path was denied.", exception);
            }
        }
    }
}
