using System.Text.Json;
using FourFoldAccountManager.Core.Data;

namespace FourFoldAccountManager.Core.Plugins.Hub;

// What the hub keeps on disk: each installed plugin's folder, the record of what is installed, and the last good
// catalog. Nothing here talks to the network or to a running plugin.
public sealed class HubStore(LocalDataPaths paths)
{
    public string FolderOf(string id) => Path.Combine(paths.HubPluginsRoot, id);

    // False when a crash mid-install left the record without its files; the next hub check installs it again.
    public bool IsIntact(string id) => File.Exists(Path.Combine(FolderOf(id), "plugin.json"));

    // No file means nothing is installed, and a file that isn't JSON reads the same way. A file that exists but can't
    // be opened throws, so a locked record is never mistaken for "nothing installed" and then written over.
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
            return [];
        }

        return (records ?? [])
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

    public async Task SaveCatalogAsync(string json, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.HubCatalogFilePath)!);
        var temporary = paths.HubCatalogFilePath + ".tmp";
        await File.WriteAllTextAsync(temporary, json, cancellationToken);
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

        // The hash matched, but it must also be the plugin the user asked for.
        var manifest = PluginManifestReader.Read(stagingFolder).Manifest;
        if (manifest is null || manifest.Id != plugin.Id || manifest.Version != plugin.Version)
        {
            TryDeleteFolder(stagingFolder);
            return "The package isn't the plugin the hub listed.";
        }

        return null;
    }

    // Puts a staged plugin in place and records it. If the swap or the record fails, the version that was installed
    // before is back in place when this throws.
    public async Task CommitAsync(HubPlugin plugin, string stagingFolder, CancellationToken cancellationToken)
    {
        // Read before anything moves: a record that can't be read stops the install here.
        var records = LoadInstalled();
        var target = FolderOf(plugin.Id);
        var previous = Path.Combine(paths.HubPluginsRoot, ".old-" + Guid.NewGuid().ToString("N"));
        var hadPrevious = Directory.Exists(target);
        if (hadPrevious)
        {
            Directory.Move(target, previous);
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
            if (hadPrevious)
            {
                Directory.Move(previous, target);
            }

            throw;
        }

        TryDeleteFolder(previous);
    }

    // The record goes first, so a crash partway leaves files nothing points at, never a record without its files.
    public async Task UninstallAsync(string id, CancellationToken cancellationToken)
    {
        await SaveInstalledAsync(LoadInstalled().Where(record => record.Id != id).ToArray(), cancellationToken);
        if (Directory.Exists(FolderOf(id)))
        {
            Directory.Delete(FolderOf(id), recursive: true);
        }

        var saved = Path.Combine(paths.PluginDataRoot, id + ".json");
        if (File.Exists(saved))
        {
            File.Delete(saved);
        }
    }

    // Removes folders a crash left behind mid-install. An installed plugin's folder never starts with a dot.
    public void CleanUp()
    {
        if (!Directory.Exists(paths.HubPluginsRoot))
        {
            return;
        }

        foreach (var folder in Directory.GetDirectories(paths.HubPluginsRoot, ".*"))
        {
            TryDeleteFolder(folder);
        }
    }

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
