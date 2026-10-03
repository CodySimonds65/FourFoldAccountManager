using System.IO.Compression;
using System.Security.Cryptography;

namespace FourFoldAccountManager.Core.Plugins.Hub;

// A plugin as the hub ships it: a zip of plugin.json and the files FourFold serves.
public static class PluginPackage
{
    private static readonly DateTimeOffset EntryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Builds the package for the plugin in folder. README, LICENSE, dot-files and other file types are left out. The
    // same files always give the same bytes. Throws InvalidDataException when the plugin is past the limits, contains
    // a link, or has a file name that Windows can't unpack.
    public static byte[] Build(string folder)
    {
        var files = new List<(string Name, string Path)>();
        Collect(folder, string.Empty, files);
        files.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        if (UnpackableReason(files.ConvertAll(file => file.Name)) is { } reason)
        {
            throw new InvalidDataException(reason);
        }

        if (files.Count > HubLimits.MaximumPackageFiles)
        {
            throw new InvalidDataException($"A plugin can have at most {HubLimits.MaximumPackageFiles} files.");
        }

        if (files.Sum(file => new FileInfo(file.Path).Length) > HubLimits.MaximumUnpackedBytes)
        {
            throw new InvalidDataException("A plugin's files can be at most 5 MB together.");
        }

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, path) in files)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = EntryTime;
                using var output = entry.Open();
                output.Write(File.ReadAllBytes(path));
            }
        }

        return buffer.ToArray();
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // Whether a downloaded package is the one the catalog names.
    public static bool Matches(byte[] package, long size, string sha256) =>
        package.LongLength == size && string.Equals(Sha256(package), sha256, StringComparison.Ordinal);

    // Why files with these names (paths inside a package, with '/') can't all be unpacked on Windows, or null. The hub
    // tool runs on Linux, where Path.GetInvalidFileNameChars lists only '/' and NUL, so the Windows rules are written
    // out here; without this a package would build fine and then be refused on a user's machine.
    public static string? UnpackableReason(IReadOnlyList<string> names)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            foreach (var part in name.Split('/'))
            {
                if (part.Any(character => char.IsControl(character) || "\\:<>\"|?*".Contains(character)) ||
                    part.EndsWith('.') || part.EndsWith(' '))
                {
                    return $"{name} can't be unpacked on Windows: a file or folder name can't contain \\ : < > \" | ? * " +
                           "or a control character, or end with a dot or a space.";
                }
            }

            if (!seen.TryAdd(name, name))
            {
                return $"{name} and {seen[name]} differ only by capital letters, which Windows can't tell apart.";
            }
        }

        return null;
    }

    // Unpacks a package into targetFolder. Throws InvalidDataException or IOException when it breaks a rule; the
    // caller deletes the folder. A package holds only plugin.json and the file types FourFold serves, as Build
    // makes it; anything else, such as an .exe, is refused before a byte is written.
    public static void Extract(byte[] package, string targetFolder)
    {
        using var stream = new MemoryStream(package);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (archive.Entries.Any(entry =>
                    !entry.FullName.EndsWith('/') && entry.FullName != "plugin.json" &&
                    !PluginFileServer.IsServed(entry.FullName)))
            {
                throw new InvalidDataException("The package holds a file that FourFold doesn't serve.");
            }
        }

        SafeArchive.Extract(
            stream, targetFolder, HubLimits.MaximumPackageFiles, HubLimits.MaximumUnpackedBytes, stripTopFolder: false);
    }

    private static void Collect(string folder, string prefix, List<(string Name, string Path)> files)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(folder))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.'))
            {
                continue;
            }

            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException("A plugin can't contain links.");
            }

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                Collect(path, prefix + name + "/", files);
            }
            else if (prefix + name == "plugin.json" || PluginFileServer.IsServed(name))
            {
                files.Add((prefix + name, path));
            }
        }
    }
}
