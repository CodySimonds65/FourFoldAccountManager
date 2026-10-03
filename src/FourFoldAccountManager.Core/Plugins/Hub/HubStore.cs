using System.Text;
using System.Text.Json;
using FourFoldAccountManager.Core.Data;

namespace FourFoldAccountManager.Core.Plugins.Hub;

// What the hub keeps on disk: each installed plugin's folder, the record of what is installed, and the last good
// catalog. Nothing here talks to the network or to a running plugin.
public sealed class HubStore(LocalDataPaths paths)
{
    // Git's null id. A record rebuilt from the folders on disk can't know which commit they hold, so it names this
    // one: it passes the commit check, never equals a commit the catalog lists (so it never earns any-website trust),
    // and the next hub check sees a different commit and installs the plugin again, cleanly.
    public static readonly string UnknownCommit = new('0', 40);

    private const string OldPrefix = ".old-";

    // Commit and uninstall each read the record, wait, then write it, so two at once would lose one of the writes.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Throws for anything but a plugin id, so no caller can point this (or the recursive delete in UninstallAsync) at
    // a folder outside the plugins folder.
    public string FolderOf(string id) => PluginManifestReader.IsValidId(id)
        ? Path.Combine(paths.HubPluginsRoot, id)
        : throw new ArgumentException("That isn't a plugin id.", nameof(id));

    // False when the folder is missing or broken: a crash mid-install left the record without its files, or a file the
    // plugin needs is gone. The next hub check installs it again.
    public bool IsIntact(string id) => PluginManifestReader.Read(FolderOf(id)).Manifest is not null;

    // No file means nothing is installed. A record that is not JSON, is empty, or holds an entry of the wrong shape is
    // rebuilt from the plugin folders on disk, so a damaged record never makes the next install forget the others; an
    // entry whose id or commit isn't valid is left out. A file that exists but can't be opened throws (IOException, or
    // UnauthorizedAccessException when access is denied), so a locked record is never mistaken for "nothing installed"
    // and then written over.
    public IReadOnlyList<HubInstalled> LoadInstalled()
    {
        if (!File.Exists(paths.HubInstalledFilePath))
        {
            return [];
        }

        List<HubInstalled?>? records;
        try
        {
            records = JsonSerializer.Deserialize<List<HubInstalled?>>(
                File.ReadAllText(paths.HubInstalledFilePath), AtomicJsonFile.Options);
        }
        catch (JsonException)
        {
            records = null;
        }

        return records is null
            ? RebuildFromFolders()
            : records
                .Where(record => record is not null && PluginManifestReader.IsValidId(record.Id) &&
                                 HubCatalogJson.IsCommit(record.Commit))
                .Select(record => record!)
                .DistinctBy(record => record.Id)
                .ToArray();
    }

    public HubCatalog? LoadCatalog()
    {
        try
        {
            return File.Exists(paths.HubCatalogFilePath)
                ? HubCatalogJson.Parse(File.ReadAllText(paths.HubCatalogFilePath))
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Written through to disk before it takes the final name, so a power cut can't leave a short file there.
    public async Task SaveCatalogAsync(string json, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.HubCatalogFilePath)!);
        var temporary = paths.HubCatalogFilePath + ".tmp";
        await using (var stream = new FileStream(
            temporary, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, paths.HubCatalogFilePath, overwrite: true);
    }

    // Checks a downloaded package and unpacks it into a staging folder beside the installed plugins. Returns the
    // reason it was refused, or null. Nothing that is installed is touched.
    public string? Stage(HubPlugin plugin, byte[] package, out string stagingFolder)
    {
        stagingFolder = Path.Combine(paths.HubPluginsRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        if (!PluginPackage.Matches(package, plugin.Size, plugin.Sha256))
        {
            return "The download didn't match the hub's record.";
        }

        try
        {
            PluginPackage.Extract(package, stagingFolder);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            TryDeleteFolder(stagingFolder);
            return "The package isn't valid.";
        }

        // The hash matched, but it must also be the plugin the user asked for, asking for the websites the hub page
        // showed: the catalog's sites are what the user saw, and the plugin.json is what the sandbox enforces.
        var read = PluginManifestReader.Read(stagingFolder);
        var manifest = read.Manifest;
        if (manifest is null || manifest.Id != plugin.Id || manifest.Version != plugin.Version ||
            !manifest.Sites.ToHashSet().SetEquals(plugin.Sites) || (plugin.AnySite && !manifest.AnySite))
        {
            TryDeleteFolder(stagingFolder);
            return read.Error == PluginManifestReader.NeedsNewerFourFold
                ? "This version needs a newer FourFold."
                : "The package isn't the plugin the hub listed.";
        }

        return null;
    }

    // Puts a staged plugin in place and records it. If the swap or the record fails, the version that was installed
    // before is back in place when this throws, and the staging folder is gone.
    public async Task CommitAsync(HubPlugin plugin, string stagingFolder, CancellationToken cancellationToken)
    {
        var target = FolderOf(plugin.Id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // The id is in the name so CleanUp can put the folder back if a crash comes between the two moves.
            var previous = Path.Combine(paths.HubPluginsRoot, $"{OldPrefix}{plugin.Id}-{Guid.NewGuid():N}");
            IReadOnlyList<HubInstalled> records;
            bool hadPrevious;
            try
            {
                // Read before anything moves: a record that can't be read stops the install here.
                records = LoadInstalled();
                hadPrevious = Directory.Exists(target);
                if (hadPrevious)
                {
                    Directory.Move(target, previous);
                }
            }
            catch
            {
                // Nothing was swapped, so only the staged files need to go.
                TryDeleteFolder(stagingFolder);
                throw;
            }

            try
            {
                Directory.Move(stagingFolder, target);
                try
                {
                    await SaveInstalledAsync(
                        records.Where(record => record.Id != plugin.Id)
                            .Append(new HubInstalled(plugin.Id, plugin.Commit))
                            .ToArray(),
                        cancellationToken);
                }
                catch
                {
                    Directory.Move(target, stagingFolder);
                    throw;
                }
            }
            catch
            {
                // If putting the old version back fails, nothing below runs and the staged files stay.
                if (hadPrevious)
                {
                    Directory.Move(previous, target);
                }

                TryDeleteFolder(stagingFolder);
                throw;
            }

            TryDeleteFolder(previous);
        }
        finally
        {
            _gate.Release();
        }
    }

    // The record goes first, so a crash partway leaves files nothing points at, never a record without its files. The
    // folder is renamed before it is deleted: a delete that fails partway then leaves a leftover CleanUp finishes at
    // the next start, never a half-deleted plugin folder that could be loaded. A folder that can't be renamed stays
    // under its own name, unrecorded, and installing the plugin again replaces it.
    public async Task UninstallAsync(string id, CancellationToken cancellationToken)
    {
        var folder = FolderOf(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveInstalledAsync(LoadInstalled().Where(record => record.Id != id).ToArray(), cancellationToken);
            var saved = Path.Combine(paths.PluginDataRoot, id + ".json");
            if (File.Exists(saved))
            {
                File.Delete(saved);
            }

            if (Directory.Exists(folder))
            {
                var trash = Path.Combine(paths.HubPluginsRoot, ".trash-" + Guid.NewGuid().ToString("N"));
                Directory.Move(folder, trash);
                TryDeleteFolder(trash);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Clears what a crash or a failed delete left behind. An installed plugin's folder never starts with a dot. A
    // folder CommitAsync set aside is put back when the swap never finished and the plugin has no folder; otherwise it
    // is deleted with the rest. Never throws: a plugins folder that can't be listed must not stop the hub starting.
    public void CleanUp()
    {
        try
        {
            if (!Directory.Exists(paths.HubPluginsRoot))
            {
                return;
            }

            foreach (var folder in Directory.GetDirectories(paths.HubPluginsRoot, ".*"))
            {
                if (SetAsideId(Path.GetFileName(folder)) is { } id && !Directory.Exists(FolderOf(id)))
                {
                    try
                    {
                        Directory.Move(folder, FolderOf(id));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Kept, never deleted: it may be the only copy. The next start tries again.
                    }

                    continue;
                }

                TryDeleteFolder(folder);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next start tries again.
        }
    }

    // The plugin id in ".old-<id>-<32 hex digits>", or null for any other name.
    private static string? SetAsideId(string name)
    {
        if (!name.StartsWith(OldPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = name[OldPrefix.Length..];
        return rest.Length > 33 && rest[^33] == '-' && Guid.TryParseExact(rest[^32..], "N", out _) &&
               PluginManifestReader.IsValidId(rest[..^33])
            ? rest[..^33]
            : null;
    }

    private IReadOnlyList<HubInstalled> RebuildFromFolders() =>
        !Directory.Exists(paths.HubPluginsRoot)
            ? []
            : Directory.GetDirectories(paths.HubPluginsRoot)
                .Select(folder => Path.GetFileName(folder))
                .Where(name => !name.StartsWith('.') && PluginManifestReader.IsValidId(name) && IsIntact(name))
                .Order(StringComparer.Ordinal)
                .Select(name => new HubInstalled(name, UnknownCommit))
                .ToArray();

    private Task SaveInstalledAsync(IReadOnlyList<HubInstalled> records, CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(paths.HubInstalledFilePath, records, cancellationToken);

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // CleanUp tries again at the next start.
        }
    }
}
