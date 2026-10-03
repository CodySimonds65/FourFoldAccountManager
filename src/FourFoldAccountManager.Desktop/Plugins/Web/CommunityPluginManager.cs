using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Plugins;

namespace FourFoldAccountManager.Desktop.Plugins.Web;

public sealed record RejectedPlugin(string FolderName, string Reason);

// Loads community plugins from the dev plugins folder while developer mode is on, runs the ones that are switched on,
// and reloads a plugin when its files change. Create and use it on the UI thread.
public sealed class CommunityPluginManager : IDisposable
{
    private readonly LocalDataPaths _paths;
    private readonly IPluginHostData _host;
    private readonly Panel _parkingHost;
    private readonly PluginCardStore _cards;
    private readonly PluginBrowser _browser;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _reloadTimer;
    private readonly HashSet<string> _changedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WebPlugin> _plugins = [];
    private readonly List<RejectedPlugin> _rejected = [];
    private FileSystemWatcher? _watcher;
    private bool _disposed;
    private PanelSettings _settings = PanelSettings.Default;

    public CommunityPluginManager(LocalDataPaths paths, IPluginHostData host, Panel parkingHost, PluginCardStore cards)
    {
        _paths = paths;
        _host = host;
        _parkingHost = parkingHost;
        _cards = cards;
        _browser = new PluginBrowser(paths);
        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _reloadTimer.Tick += async (_, _) =>
        {
            try
            {
                await ReloadChangedAsync();
            }
            catch (Exception)
            {
                // Nothing from a plugin folder may crash the app, and the app has no global exception handler.
            }
        };
    }

    public IReadOnlyList<WebPlugin> Plugins => _plugins;

    public IReadOnlyList<RejectedPlugin> Rejected => _rejected;

    // Set when the dev folder can't be read or the plugin browser can't start; the plugin list shows it.
    public string? StartupError { get; private set; }

    // Raised when the set of plugins, or whether one is running, changed.
    public event Action? Changed;

    // Matches what is loaded and running to the settings. Never throws: a folder that can't be read, or a plugin that
    // can't start, is reported through StartupError.
    public async Task ApplyAsync(PanelSettings settings)
    {
        // A click while the window is closing must not build new web views.
        if (_disposed)
        {
            return;
        }

        _settings = settings;
        if (!settings.PluginDeveloperMode)
        {
            var hadState = _plugins.Count > 0 || _rejected.Count > 0 || StartupError is not null;
            UnloadAll();
            StartupError = null;
            if (hadState)
            {
                Changed?.Invoke();
            }

            return;
        }

        if (_watcher is null)
        {
            try
            {
                Directory.CreateDirectory(_paths.DevPluginsRoot);
                Rescan(changedFolders: null);
                StartWatching();
            }
            catch (Exception)
            {
                AbandonLoad();
                Changed?.Invoke();
                return;
            }

            Changed?.Invoke();
        }

        if (await SyncRunningAsync())
        {
            Changed?.Invoke();
        }
    }

    public void PostEvent(string name, object? data)
    {
        foreach (var plugin in _plugins)
        {
            plugin.PostEvent(name, data);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        UnloadAll();
    }

    // Re-reads the dev folder. A plugin whose folder is in changedFolders (or every plugin, when it is null) is
    // disposed and read again; the rest are kept running.
    private void Rescan(IReadOnlySet<string>? changedFolders)
    {
        var folders = Directory.Exists(_paths.DevPluginsRoot)
            ? Directory.GetDirectories(_paths.DevPluginsRoot).OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        var kept = _plugins
            .Where(plugin => folders.Contains(plugin.Manifest.Folder, StringComparer.OrdinalIgnoreCase) &&
                             changedFolders is not null &&
                             !changedFolders.Contains(Path.GetFileName(plugin.Manifest.Folder)))
            .ToList();
        Unload(_plugins.Except(kept).ToArray());
        _rejected.Clear();
        foreach (var folder in folders)
        {
            if (kept.Any(plugin => plugin.Manifest.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var folderName = Path.GetFileName(folder);
            PluginManifestResult result;
            try
            {
                result = PluginManifestReader.Read(folder);
            }
            catch (Exception)
            {
                // One folder that can't be read must never take the others down.
                _rejected.Add(new RejectedPlugin(folderName, "plugin.json couldn't be read."));
                continue;
            }

            if (result.Manifest is not { } manifest)
            {
                _rejected.Add(new RejectedPlugin(folderName, result.Error ?? "plugin.json couldn't be read."));
                continue;
            }

            if (kept.Any(plugin => plugin.Manifest.Id == manifest.Id))
            {
                _rejected.Add(new RejectedPlugin(folderName, $"Another plugin already uses the id '{manifest.Id}'."));
                continue;
            }

            kept.Add(new WebPlugin(manifest, PluginTrust.Developer, _browser, _parkingHost, _host, _cards, _paths));
        }

        _plugins.Clear();
        _plugins.AddRange(kept.OrderBy(plugin => plugin.Manifest.Folder, StringComparer.OrdinalIgnoreCase));
    }

    private void Unload(IReadOnlyList<WebPlugin> plugins)
    {
        foreach (var plugin in plugins)
        {
            plugin.Dispose();
            _plugins.Remove(plugin);
        }
    }

    private void UnloadAll()
    {
        StopWatching();
        Unload(_plugins.ToArray());
        _rejected.Clear();
    }

    // The dev folder can't be read or watched. Nothing stays half-loaded, and the next apply tries again.
    private void AbandonLoad()
    {
        UnloadAll();
        StartupError = "The dev plugins folder couldn't be read.";
    }

    // Starts switched-on plugins and stops switched-off ones. Returns whether anything changed.
    private async Task<bool> SyncRunningAsync()
    {
        var changed = StartupError is not null;
        StartupError = null;
        foreach (var plugin in _plugins.ToArray())
        {
            // A reload or developer mode switching off during an earlier await disposed this one.
            if (!_plugins.Contains(plugin))
            {
                continue;
            }

            var shouldRun = PluginLayoutPolicy.IsEnabled(_settings, plugin.Descriptor.Id);
            if (shouldRun && !plugin.IsRunning)
            {
                try
                {
                    await plugin.StartAsync();
                    // The user may have switched it off while it was starting.
                    if (plugin.IsRunning && !PluginLayoutPolicy.IsEnabled(_settings, plugin.Descriptor.Id))
                    {
                        plugin.Stop();
                    }

                    // A start that was abandoned (the plugin was stopped meanwhile) returns normally, so ask the plugin.
                    changed |= plugin.IsRunning;
                }
                catch (Exception)
                {
                    StartupError = "Community plugins couldn't start.";
                    changed = true;
                }
            }
            else if (!shouldRun && plugin.IsRunning)
            {
                plugin.Stop();
                changed = true;
            }
        }

        return changed;
    }

    private void StartWatching()
    {
        var watcher = new FileSystemWatcher(_paths.DevPluginsRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Created += (_, args) => QueueReload(args.FullPath);
        watcher.Changed += (_, args) => QueueReload(args.FullPath);
        watcher.Deleted += (_, args) => QueueReload(args.FullPath);
        watcher.Renamed += (_, args) =>
        {
            QueueReload(args.OldFullPath);
            QueueReload(args.FullPath);
        };
        // After a missed burst of changes, or when the folder itself went away, start over with a fresh watcher.
        watcher.Error += (sender, args) => _dispatcher.BeginInvoke(() =>
        {
            // Only the current watcher counts; a late error from a replaced one must not stop its successor.
            if (!ReferenceEquals(sender, _watcher))
            {
                return;
            }

            StopWatching();
            _ = ApplyAsync(_settings);
        });
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private void StopWatching()
    {
        _reloadTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        lock (_changedFolders)
        {
            _changedFolders.Clear();
        }
    }

    // Watcher events arrive on a background thread; a save often fires several, so reloads are debounced.
    private void QueueReload(string fullPath)
    {
        var relative = Path.GetRelativePath(_paths.DevPluginsRoot, fullPath);
        var folderName = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        lock (_changedFolders)
        {
            _changedFolders.Add(folderName);
        }

        _dispatcher.BeginInvoke(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
    }

    private async Task ReloadChangedAsync()
    {
        _reloadTimer.Stop();
        if (_watcher is null)
        {
            return;
        }

        HashSet<string> changed;
        lock (_changedFolders)
        {
            changed = new HashSet<string>(_changedFolders, StringComparer.OrdinalIgnoreCase);
            _changedFolders.Clear();
        }

        try
        {
            Rescan(changed);
        }
        catch (Exception)
        {
            // A tick on the UI thread must never take the app down, whatever state the folder was left in.
            AbandonLoad();
            Changed?.Invoke();
            return;
        }

        await SyncRunningAsync();
        Changed?.Invoke();
    }
}
