using System.IO.Compression;

namespace FourFoldAccountManager.Core.Plugins.Hub;

// Unpacks a zip that came from the network. Anything that could write outside the target folder, or fill the disk,
// throws InvalidDataException before it is written. The caller deletes the target folder when this throws.
public static class SafeArchive
{
    // stripTopFolder drops each entry's first path part, for GitHub's repository zips, which wrap everything in one
    // "<repo>-<commit>" folder.
    public static void Extract(Stream zip, string targetFolder, int maximumFiles, long maximumBytes, bool stripTopFolder)
    {
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

            // A Unix symbolic link is stored as a file whose mode bits say "link".
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new InvalidDataException("The archive contains a link.");
            }

            var parts = name.Split('/');
            if (name.Contains('\\') || name.Contains(':') ||
                parts.Any(part => part.Length == 0 || part is "." or ".."))
            {
                throw new InvalidDataException("The archive contains a path that isn't allowed.");
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
    }
}
