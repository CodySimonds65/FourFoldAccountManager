using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Core.Plugins.Hub;

namespace FourFoldAccountManager.Desktop.Plugins.Web;

public sealed record RejectedPlugin(string FolderName, string Reason);

// Runs community plugins from two places: the plugins installed from the hub, and, while developer mode is on, the
// dev plugins folder (reloading a plugin when its files change). Create and use it on the UI thread.
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
    private readonly List<WebPlugin> _dev = [];
    private readonly List<WebPlugin> _hub = [];
    private readonly Dictionary<WebPlugin, HubLoad> _hubLoadOf = [];
    private readonly List<RejectedPlugin> _rejected = [];

    // One settings store per plugin id, so a hub plugin, its update and a dev copy of it never write the same file
    // from two stores.
    private readonly Dictionary<string, PluginStorage> _storages = new(StringComparer.Ordinal);
    private IReadOnlyList<HubLoad> _hubLoads = [];
    private FileSystemWatcher? _watcher;
    private bool _disposed;
    private bool _changedQueued;
    private string? _folderError;
    private string? _startError;
    private PanelSettings _settings = PanelSettings.Default;

    // False until the first ApplyAsync: before that _settings is only the default, which switches everything on.
    private bool _settingsGiven;

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

    // Dev-folder plugins first, then hub plugins.
    public IReadOnlyList<WebPlugin> Plugins => [.. _dev, .. _hub];

    public IReadOnlyList<RejectedPlugin> Rejected => _rejected;

    // Set when the dev folder can't be read or a plugin can't start; the plugin list shows it.
    public string? StartupError => _folderError ?? _startError;

    // Raised when the set of plugins, or whether one is running, changed.
    public event Action? Changed;

    // Matches the dev-folder plugins, and what is running, to the settings. Never throws: a folder that can't be
    // read, or a plugin that can't start, is reported through StartupError.
    public async Task ApplyAsync(PanelSettings settings)
    {
        // A click while the window is closing must not build new web views.
        if (_disposed)
        {
            return;
        }

        _settings = settings;
        _settingsGiven = true;
        if (!settings.PluginDeveloperMode)
        {
            if (_dev.Count > 0 || _rejected.Count > 0 || _watcher is not null || _folderError is not null)
            {
                UnloadDev();
                _folderError = null;
                // A hub plugin that a dev copy was standing in for comes back.
                ReconcileHub();
                Changed?.Invoke();
            }
        }
        else if (_watcher is null)
        {
            try
            {
                Directory.CreateDirectory(_paths.DevPluginsRoot);
                Rescan(changedFolders: null);
                StartWatching();
                _folderError = null;
            }
            catch (Exception)
            {
                AbandonLoad();
            }

            ReconcileHub();
            Changed?.Invoke();
        }

        if (await SyncRunningAsync())
        {
            Changed?.Invoke();
        }
    }

    // The installed hub plugins that may run. Never throws.
    public async Task SetHubPluginsAsync(IReadOnlyList<HubLoad> loads)
    {
        if (_disposed)
        {
            return;
        }

        _hubLoads = loads;
        ReconcileHub();
        Changed?.Invoke();
        if (await SyncRunningAsync())
        {
            Changed?.Invoke();
        }
    }

    // Stops a hub plugin and lets go of its folder, before the folder is swapped or deleted. It stays unloaded until
    // the next SetHubPluginsAsync.
    public void Unload(string id)
    {
        // After Dispose the plugins are gone for good; reconciling would load the other hub plugins again.
        if (_disposed)
        {
            return;
        }

        _hubLoads = _hubLoads.Where(load => load.Id != id).ToArray();
        ReconcileHub();
        Changed?.Invoke();
    }

    // After an uninstall deleted the plugin's settings file: the store that cached it must not write it back.
    public void Forget(string id) => _storages.Remove(id);

    // Best effort, and bounded: an uninstall that awaits this must not hang if the borrowed web view never starts.
    public async Task ClearBrowserDataAsync(string id)
    {
        if (_disposed)
        {
            return;
        }

        await Task.WhenAny(
            _browser.ClearOriginAsync(_parkingHost, PluginNetworkPolicy.Origin(id)), Task.Delay(TimeSpan.FromSeconds(10)));
    }

    public void PostEvent(string name, object? data)
    {
        foreach (var plugin in Plugins)
        {
            plugin.PostEvent(name, data);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        UnloadDev();
        foreach (var plugin in _hub)
        {
            plugin.Dispose();
        }

        _hub.Clear();
        _hubLoadOf.Clear();
    }

    // Makes the loaded hub plugins match the loads that may run and aren't stood in for by a dev plugin. A load whose
    // folder, commit or trust changed is a different load, so its plugin is replaced.
    private void ReconcileHub()
    {
        var wanted = HubPolicy.Visible(_hubLoads, _dev.Select(plugin => plugin.Manifest.Id).ToHashSet(StringComparer.Ordinal));
        foreach (var plugin in _hub.ToArray())
        {
            if (!wanted.Contains(_hubLoadOf[plugin]))
            {
                plugin.Dispose();
                _hub.Remove(plugin);
                _hubLoadOf.Remove(plugin);
            }
        }

        foreach (var load in wanted)
        {
            if (_hubLoadOf.ContainsValue(load))
            {
                continue;
            }

            // An install that was damaged on disk simply doesn't load; the next hub check installs it again.
            var manifest = PluginManifestReader.Read(load.Folder).Manifest;
            if (manifest is null || manifest.Id != load.Id)
            {
                continue;
            }

            try
            {
                var plugin = Create(manifest, load.Trust);
                _hub.Add(plugin);
                _hubLoadOf[plugin] = load;
            }
            catch (Exception)
            {
                // One plugin that can't be loaded must never take the others down.
            }
        }

        // A replaced plugin was appended; the list keeps the order of the loads, so an update never moves a plugin.
        var order = wanted.ToList();
        var ordered = _hub.OrderBy(plugin => order.IndexOf(_hubLoadOf[plugin])).ToArray();
        _hub.Clear();
        _hub.AddRange(ordered);
    }

    private WebPlugin Create(PluginManifest manifest, PluginTrust trust)
    {
        if (!_storages.TryGetValue(manifest.Id, out var storage))
        {
            storage = new PluginStorage(Path.Combine(_paths.PluginDataRoot, manifest.Id + ".json"));
            _storages[manifest.Id] = storage;
        }

        var plugin = new WebPlugin(manifest, trust, _browser, _parkingHost, _host, _cards, storage);
        // A plugin stops or restarts by itself (a flood, a crash, its Reload button). That can happen inside a web view
        // event or mid-start, and Changed re-renders the sidebar, so it is raised later, once for however many plugins
        // changed in the meantime.
        plugin.RunningChanged += () =>
        {
            if (_changedQueued)
            {
                return;
            }

            _changedQueued = true;
            _dispatcher.BeginInvoke(() =>
            {
                _changedQueued = false;
                if (!_disposed)
                {
                    // The Reload notice starts a plugin directly. If the user switched it off while it was starting,
                    // the sync has already passed, so it is stopped here. Stopping raises RunningChanged and queues
                    // one more pass, which finds nothing running that is switched off.
                    if (_settingsGiven)
                    {
                        foreach (var loaded in Plugins)
                        {
                            if (loaded.IsRunning && !PluginLayoutPolicy.IsEnabled(_settings, loaded.Descriptor.Id))
                            {
                                loaded.Stop();
                            }
                        }
                    }

                    Changed?.Invoke();
                }
            });
        };
        return plugin;
    }

    // Re-reads the dev folder. A plugin whose folder is in changedFolders (or every plugin, when it is null) is
    // disposed and read again; the rest are kept running.
    private void Rescan(IReadOnlySet<string>? changedFolders)
    {
        var folders = Directory.Exists(_paths.DevPluginsRoot)
            ? Directory.GetDirectories(_paths.DevPluginsRoot).OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        var kept = _dev
            .Where(plugin => folders.Contains(plugin.Manifest.Folder, StringComparer.OrdinalIgnoreCase) &&
                             changedFolders is not null &&
                             !changedFolders.Contains(Path.GetFileName(plugin.Manifest.Folder)))
            .ToList();
        foreach (var plugin in _dev.Except(kept).ToArray())
        {
            plugin.Dispose();
        }

        _rejected.Clear();
        foreach (var folder in folders)
        {
            if (kept.Any(plugin => plugin.Manifest.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var folderName = Path.GetFileName(folder);
            var result = PluginManifestReader.Read(folder);
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

            try
            {
                kept.Add(Create(manifest, PluginTrust.Developer));
            }
            catch (Exception)
            {
                // One folder that can't be loaded must never take the others down.
                _rejected.Add(new RejectedPlugin(folderName, "The plugin couldn't be loaded."));
            }
        }

        _dev.Clear();
        _dev.AddRange(kept.OrderBy(plugin => plugin.Manifest.Folder, StringComparer.OrdinalIgnoreCase));
    }

    private void UnloadDev()
    {
        StopWatching();
        foreach (var plugin in _dev)
        {
            plugin.Dispose();
        }

        _dev.Clear();
        _rejected.Clear();
    }

    // The dev folder can't be read or watched. No dev plugin stays half-loaded, and the next apply tries again.
    private void AbandonLoad()
    {
        UnloadDev();
        _folderError = "The dev plugins folder couldn't be read.";
    }

    // Starts switched-on plugins and stops switched-off ones. Returns whether anything changed.
    private async Task<bool> SyncRunningAsync()
    {
        // Until the user's settings are known, a plugin they switched off could be started; ApplyAsync starts them all.
        if (!_settingsGiven)
        {
            return false;
        }

        var changed = _startError is not null;
        _startError = null;
        var starts = new List<Task<bool>>();
        foreach (var plugin in Plugins)
        {
            var shouldRun = PluginLayoutPolicy.IsEnabled(_settings, plugin.Descriptor.Id);
            if (shouldRun && !plugin.IsRunning)
            {
                starts.Add(StartAsync(plugin));
            }
            else if (!shouldRun && plugin.IsRunning)
            {
                plugin.Stop();
                changed = true;
            }
        }

        // Side by side, so one plugin that hangs while starting can't hold up the rest.
        foreach (var started in await Task.WhenAll(starts))
        {
            changed |= started;
        }

        return changed;
    }

    // Starts one plugin. A plugin disposed meanwhile (a reload, an update, developer mode switching off) refuses to
    // start, and one that was abandoned mid-start returns normally, so the answer is whether it is running now.
    private async Task<bool> StartAsync(WebPlugin plugin)
    {
        try
        {
            await plugin.StartAsync();
            // The user may have switched it off while it was starting.
            if (plugin.IsRunning && !PluginLayoutPolicy.IsEnabled(_settings, plugin.Descriptor.Id))
            {
                plugin.Stop();
            }

            return plugin.IsRunning;
        }
        catch (Exception)
        {
            _startError = "Community plugins couldn't start.";
            return true;
        }
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
        }

        // A dev plugin that appeared or went away changes which hub plugins it stands in for.
        ReconcileHub();
        // Before the plugins start, so the strip never shows the ones that were just disposed.
        Changed?.Invoke();
        await SyncRunningAsync();
        Changed?.Invoke();
    }
}
