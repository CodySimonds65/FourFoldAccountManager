using System.Net.Http;
using System.Text;
using System.Windows.Threading;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Core.Plugins.Hub;
using FourFoldAccountManager.Desktop.Plugins.Web;

namespace FourFoldAccountManager.Desktop.Plugins.Hub;

// The plugin hub as the app sees it: checks the catalog, installs and updates plugins, stops the ones that were
// pulled, and tells the plugin manager which installed plugins may run. Create and use it on the UI thread. Nothing
// here throws into the app: a failure leaves things as they were and shows in ViewState.
public sealed class PluginHub : IDisposable
{
    // A hub page that is opened again and again checks the hub at most this often.
    private const long MinimumGapMilliseconds = 60_000;

    private const string RemoveFailed = "The plugin couldn't be removed.";

    // How long one whole download, body included, may take. The client's own timeout covers only the wait for the
    // response headers, so a connection that goes quiet partway through a body would otherwise hold the gate, and with
    // it every other hub operation, until the app is restarted.
    private static readonly TimeSpan DownloadLimit = TimeSpan.FromMinutes(2);

    private readonly HubStore _store;
    private readonly CommunityPluginManager _manager;

    // Its timeout bounds only the wait for a response to start; DownloadLimit bounds a whole download.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };

    // Start, refresh, install and uninstall run one at a time. Each swaps plugin folders and tells the manager what may
    // run; interleaved, one would load a plugin from the folder another is replacing, or list one that is being removed.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Plugins being installed, updated or removed, from the click until the operation ends, so a second click does
    // nothing and the page shows the plugin as busy while it waits for its turn.
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    private HubCatalog? _catalog;
    private IReadOnlyList<HubInstalled> _installed = [];
    private IReadOnlyList<HubPulledPlugin> _pulled = [];
    private bool _loading;
    private bool _unreachable;
    private bool _disposed;
    private long? _lastCheck;

    public PluginHub(LocalDataPaths paths, CommunityPluginManager manager)
    {
        _store = new HubStore(paths);
        _manager = manager;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FourFoldAccountManager");
        _timer.Tick += (_, _) => _ = RefreshAsync(userAsked: false);
    }

    public event Action? Changed;

    // A plugin was uninstalled; its cards and ids should go from the saved settings.
    public event Action<string>? Uninstalled;

    public HubViewState ViewState => new(
        _loading,
        _unreachable,
        _catalog?.Plugins ?? [],
        _installed.Select(plugin => plugin.Id).ToHashSet(StringComparer.Ordinal),
        new HashSet<string>(_busy, StringComparer.Ordinal),
        new Dictionary<string, string>(_errors, StringComparer.Ordinal),
        _pulled);

    // Reads what is installed and the last catalog from disk and starts the plugins that may run. The hub itself is
    // contacted only when something is installed; otherwise not until the hub page is opened.
    public Task StartAsync() => RunAsync(StartCoreAsync);

    // Downloads the catalog, installs approved updates, and stops plugins that were pulled.
    public Task RefreshAsync(bool userAsked)
    {
        var askedAt = Environment.TickCount64;
        return RunAsync(async () =>
        {
            if (IsDue(userAsked, askedAt))
            {
                await RefreshCoreAsync();
            }
        });
    }

    public async Task InstallAsync(string id)
    {
        if (_disposed || _catalog?.Plugins.Any(plugin => plugin.Id == id) != true || !_busy.Add(id))
        {
            return;
        }

        _errors.Remove(id);
        try
        {
            Notify();
            await RunAsync(async () =>
            {
                // The catalog may have changed while this waited for its turn.
                if (_catalog?.Plugins.FirstOrDefault(plugin => plugin.Id == id) is { } listed)
                {
                    await InstallCoreAsync(listed);
                    PushLoads();
                }
            });
        }
        finally
        {
            _busy.Remove(id);
            Notify();
        }
    }

    public async Task UninstallAsync(string id)
    {
        if (_disposed || !_busy.Add(id))
        {
            return;
        }

        _errors.Remove(id);
        try
        {
            Notify();
            await RunAsync(async () =>
            {
                // Only a plugin the record holds is removed: an uninstall also deletes the plugin's saved settings.
                if (_installed.Any(plugin => plugin.Id == id))
                {
                    await RemoveAsync(id);
                    PushLoads();
                }
            });
        }
        finally
        {
            _busy.Remove(id);
            Notify();
        }
    }

    // Doesn't wait for the gate: an operation that holds it finds _disposed set and stops at its next step, and the
    // download it may be waiting on ends when the client is disposed.
    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _http.Dispose();
    }

    // Runs one operation with the gate held. An operation that gets its turn after Dispose does nothing.
    private async Task RunAsync(Func<Task> operation)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_disposed)
            {
                await operation();
            }
        }
        catch (Exception)
        {
            // Every operation records its own failures where the hub page can show them; this is the last stop, so
            // nothing reaches the app.
        }
        finally
        {
            _gate.Release();
        }
    }

    // A refresh that waited for its turn runs only if it is still due: a user's request is met by any check that came
    // back after the request was made, and a routine one by a check within the last minute.
    private bool IsDue(bool userAsked, long askedAt) =>
        _lastCheck is not { } last ||
        (userAsked ? last < askedAt : Environment.TickCount64 - last >= MinimumGapMilliseconds);

    // Gate held.
    private async Task StartCoreAsync()
    {
        try
        {
            var (installed, catalog) = await Task.Run(() =>
            {
                _store.CleanUp();
                return (_store.LoadInstalled(), _store.LoadCatalog());
            });
            if (_disposed)
            {
                return;
            }

            _installed = installed;
            _catalog = catalog;
            PushLoads();
            if (_installed.Count > 0)
            {
                await RefreshCoreAsync();
            }
        }
        catch (Exception)
        {
            // The record of installed plugins can't be read right now. Hub plugins stay off for this session, and
            // nothing is written over it.
        }
    }

    // Gate held. Loading is true only while this runs, not while a refresh waits for its turn.
    private async Task RefreshCoreAsync()
    {
        _loading = true;
        try
        {
            Notify();
            var bytes = await DownloadAsync(HubAddresses.Catalog, HubLimits.MaximumCatalogBytes);
            if (_disposed)
            {
                return;
            }

            _lastCheck = Environment.TickCount64;
            var text = bytes is null ? null : Encoding.UTF8.GetString(bytes);
            if (HubCatalogJson.Parse(text) is not { } catalog)
            {
                // The last good catalog stays in use.
                _unreachable = true;
                return;
            }

            _unreachable = false;
            _catalog = catalog;
            try
            {
                await _store.SaveCatalogAsync(text!, CancellationToken.None);
            }
            catch (Exception)
            {
                // Offline starts fall back to the older copy; the next check saves again.
            }

            try
            {
                // Approved updates install without asking, and an install a crash left without its files is repaired.
                var due = HubPolicy.Updates(catalog, _installed)
                    .Concat(catalog.Plugins.Where(plugin =>
                        _installed.Any(other => other.Id == plugin.Id) && !_store.IsIntact(plugin.Id)))
                    .DistinctBy(plugin => plugin.Id)
                    .ToArray();
                foreach (var plugin in due)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    // A plugin the user has already asked to install or remove is theirs to finish.
                    if (!_busy.Add(plugin.Id))
                    {
                        continue;
                    }

                    try
                    {
                        await InstallCoreAsync(plugin);
                    }
                    finally
                    {
                        _busy.Remove(plugin.Id);
                        Notify();
                    }
                }
            }
            finally
            {
                // However the updates end: a plugin that was swapped and stopped runs again, and one the new catalog
                // pulled stops. Once, after all of them, so a plugin waiting for its own update isn't restarted early.
                PushLoads();
            }
        }
        catch (Exception)
        {
            _unreachable = true;
        }
        finally
        {
            _loading = false;
            Notify();
        }
    }

    // Gate held, and the plugin's id is in _busy. Downloads, checks and installs one listed plugin over whatever
    // version is there. A failure leaves the installed version in place and records a reason for the hub page. The
    // caller loads the plugin again afterwards (PushLoads).
    private async Task InstallCoreAsync(HubPlugin plugin)
    {
        _errors.Remove(plugin.Id);
        try
        {
            Notify();
            var package = await DownloadAsync(
                HubAddresses.Package(plugin.Id, plugin.Commit), Math.Min(plugin.Size, HubLimits.MaximumPackageBytes));
            if (_disposed)
            {
                return;
            }

            if (package is null)
            {
                _errors[plugin.Id] = "The download failed.";
                return;
            }

            var (reason, staging) = await Task.Run(() =>
            {
                var refused = _store.Stage(plugin, package, out var folder);
                return (refused, folder);
            });
            if (_disposed)
            {
                // The staging folder is left for the next start's CleanUp.
                return;
            }

            if (reason is not null)
            {
                _errors[plugin.Id] = reason;
                return;
            }

            // Nothing may be running from the folder while it is swapped.
            _manager.Unload(plugin.Id);
            // Off the UI thread: the swap moves folders and flushes the record to disk.
            await Task.Run(() => _store.CommitAsync(plugin, staging, CancellationToken.None));
            try
            {
                _installed = _store.LoadInstalled();
            }
            catch (Exception)
            {
                // The plugin is installed; only the re-read failed. The list stays as it was, and is read again at the
                // next install, uninstall or start. That is no failure to report.
            }
        }
        catch (Exception)
        {
            _errors[plugin.Id] = "The plugin couldn't be installed.";
        }
    }

    // One whole download under DownloadLimit. Null when it fails, is cancelled or runs out of time.
    private async Task<byte[]?> DownloadAsync(Uri address, long maximumBytes)
    {
        using var limit = new CancellationTokenSource(DownloadLimit);
        return await HubHttp.DownloadAsync(_http, address, maximumBytes, limit.Token);
    }

    // Gate held, and the plugin's id is in _busy. The record is written first by the store, so a step that fails later
    // still takes the plugin out of the record. What the manager clears and forgets, and what the saved settings forget,
    // follow the record as it is now, not how the removal ended: nothing of the plugin's is wiped while it is still
    // installed.
    private async Task RemoveAsync(string id)
    {
        try
        {
            // Nothing may be running from the folder, or holding its settings file, while they are deleted.
            _manager.Unload(id);
            // Off the UI thread: the delete walks up to 500 files and the record is flushed to disk.
            await Task.Run(() => _store.UninstallAsync(id, CancellationToken.None));
        }
        catch (Exception)
        {
            _errors[id] = RemoveFailed;
        }
        finally
        {
            try
            {
                _installed = _store.LoadInstalled();
            }
            catch (Exception)
            {
                // Whether the plugin is still recorded can't be told, so the page says it couldn't be removed.
                _errors[id] = RemoveFailed;
            }
        }

        if (!_disposed && _installed.All(plugin => plugin.Id != id))
        {
            try
            {
                await _manager.ClearBrowserDataAsync(id);
            }
            catch (Exception)
            {
                // Best effort: the plugin is gone from the record either way, and its settings must still be forgotten.
            }

            // The manager may have been disposed while the browser data was clearing.
            if (!_disposed)
            {
                _manager.Forget(id);
                Uninstalled?.Invoke(id);
            }
        }
    }

    // Gate held. Tells the manager which installed plugins may run, and works out which ones the hub no longer lists.
    private void PushLoads()
    {
        if (_disposed)
        {
            return;
        }

        _pulled = _installed
            .Select(plugin => (plugin.Id, Reason: HubPolicy.PulledReason(_catalog, plugin.Id)))
            .Where(entry => entry.Reason is not null)
            .Select(entry => new HubPulledPlugin(
                entry.Id,
                PluginManifestReader.Read(_store.FolderOf(entry.Id)).Manifest?.Name ?? entry.Id,
                entry.Reason!))
            .ToArray();
        // Checked while something is installed; with nothing installed there is nothing to keep up to date.
        if (_installed.Count > 0)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }

        // Not awaited: it finishes when every plugin has started, and one that hangs must not hold up the hub.
        _ = _manager.SetHubPluginsAsync(HubPolicy.Loads(_catalog, _installed, _store.FolderOf));
        Notify();
    }

    // Nothing is raised after Dispose: the window and the manager it would reach are going away.
    private void Notify()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }
}
