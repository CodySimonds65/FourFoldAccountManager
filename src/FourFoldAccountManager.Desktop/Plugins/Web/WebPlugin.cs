using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Plugins;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FourFoldAccountManager.Desktop.Plugins.Web;

// A community plugin: a sandboxed page in the shared plugin browser. The page keeps running while its panel isn't
// showing (its web view waits in a small invisible parking host), so its cards stay up to date.
public sealed class WebPlugin : IFourFoldPlugin, IDisposable
{
    // A page can post messages or ask for files in a tight loop, faster than the UI thread can answer, which freezes
    // the whole app (and again on every launch). More than this many events, or this much message and reply text,
    // within one second stops the plugin; its Reload button starts it fresh. Replies count too: a loop of reads that
    // each return a big stored value saturates the UI thread well before the event limit.
    private const int MaximumEventsPerSecond = 2_000;
    private const int MaximumMessageCharactersPerSecond = 8 * 1024 * 1024;

    // Serving a file reads it whole on the UI thread, so a larger one is treated as missing, and a page that asks for
    // more than this much of its own files within one second is stopped like any other flood.
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private const long MaximumServedBytesPerSecond = 32 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PluginTrust _trust;
    private readonly PluginBrowser _browser;
    private readonly Panel _parkingHost;
    private readonly PluginHttpFetcher _http;
    private readonly PluginApi _api;
    private readonly string _contentSecurityPolicy;
    private readonly Border _root;
    private readonly Grid _viewHost = new();
    private readonly StackPanel _stoppedNotice;
    private WebView2CompositionControl? _view;

    // Stop moves the generation on. A start that finds it changed after an await was stopped meanwhile, so it tears
    // down the view it was building instead of bringing the plugin up. _startingGeneration is the generation whose
    // start is in flight, so a second start for the same generation does nothing.
    private int _generation;
    private int _startingGeneration = -1;

    // A disposed plugin is never started again, whoever still holds it (a manager loop that was mid-await, say).
    private bool _disposed;

    // The current one-second window of the flood limits.
    private long _windowStart;
    private int _windowEvents;
    private long _windowCharacters;
    private long _windowServedBytes;

    public WebPlugin(
        PluginManifest manifest,
        PluginTrust trust,
        PluginBrowser browser,
        Panel parkingHost,
        IPluginHostData host,
        PluginCardStore cards,
        LocalDataPaths paths)
    {
        Manifest = manifest;
        _trust = trust;
        _browser = browser;
        _parkingHost = parkingHost;
        _http = new PluginHttpFetcher(manifest, trust);
        _api = new PluginApi(
            manifest, host, new PluginStorage(Path.Combine(paths.PluginDataRoot, manifest.Id + ".json")), _http, cards,
            TimeProvider.System);
        _contentSecurityPolicy = PluginNetworkPolicy.BuildContentSecurityPolicy(manifest, trust);
        Descriptor = new PluginDescriptor(manifest.Id, manifest.Name, manifest.ShortLabel, "", [], [])
        {
            Cards = manifest.Cards.Select(card => new PluginCardDescriptor(card.Id, card.Name, card.Scope)).ToArray(),
            IconPath = manifest.Icon is null ? null : Path.Combine(manifest.Folder, manifest.Icon),
            Badge = trust == PluginTrust.Developer ? "DEV" : null,
            // IdnHost, so a look-alike Unicode host name shows as its real xn-- form.
            Detail = PluginNetworkPolicy.AllowsAnySite(manifest, trust) ? "Can contact: any website"
                : manifest.Sites.Count == 0 ? "Can contact: no websites"
                : "Can contact: " + string.Join(", ", manifest.Sites.Select(site =>
                    site.IsDefaultPort ? site.IdnHost : $"{site.IdnHost}:{site.Port}"))
        };

        var reload = new Button { Content = "Reload", Height = 30, Padding = new Thickness(12, 0, 12, 0) };
        reload.SetResourceReference(FrameworkElement.StyleProperty, "AppButtonStyle");
        reload.Click += async (_, _) => await RestartAsync();
        _stoppedNotice = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Children =
            {
                new TextBlock { Text = "This plugin stopped.", Margin = new Thickness(0, 0, 0, 10) },
                reload
            }
        };
        _viewHost.Children.Add(_stoppedNotice);
        _root = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = _viewHost };
        _root.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        _root.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        // The view lives in the panel while the panel is on screen, and in the parking host the rest of the time. A web
        // view in a hidden tree is throttled by the browser, which would stall the page.
        _root.IsVisibleChanged += (_, _) => MoveView(_root.IsVisible ? _viewHost : _parkingHost);
    }

    public PluginManifest Manifest { get; }

    public PluginDescriptor Descriptor { get; }

    public FrameworkElement Panel => _root;

    public FrameworkElement? SettingsPage => null;

    public bool IsRunning => _view is not null;

    // Raised when the plugin stops (a flood, a crash, Reload, being switched off) and when it has started. The manager
    // forwards it, so the cards' stale dimming follows IsRunning.
    public event Action? RunningChanged;

    public void Opened()
    {
    }

    public async Task StartAsync()
    {
        if (_disposed || _view is not null || _startingGeneration == _generation)
        {
            return;
        }

        var generation = _startingGeneration = _generation;
        WebView2CompositionControl? view = null;
        var started = false;
        try
        {
            var environment = await _browser.GetEnvironmentAsync();
            if (generation != _generation)
            {
                return;
            }

            // The panel's colour, so there is no white flash before the page's own background loads.
            view = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x17, 0x1D, 0x24) };
            (_root.IsVisible ? _viewHost : _parkingHost).Children.Add(view);
            await view.EnsureCoreWebView2Async(environment);
            if (generation != _generation)
            {
                return;
            }

            var core = view.CoreWebView2;
            var developer = _trust == PluginTrust.Developer;
            core.Settings.AreDevToolsEnabled = developer;
            core.Settings.AreDefaultContextMenusEnabled = developer;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            // No alert, confirm or prompt: a plugin can't pose as FourFold.
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.AddWebResourceRequestedFilter(
                "*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (sender, args) => OnWebResourceRequested(sender, environment, args);
            core.NavigationStarting += (_, args) => args.Cancel = !IsOwnOrigin(args.Uri);
            core.FrameNavigationStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
            core.ClientCertificateRequested += (_, args) =>
            {
                args.Cancel = true;
                args.Handled = true;
            };
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
            core.ScreenCaptureStarting += (_, args) => args.Cancel = true;
            // Script dialogs are off, and an unanswered "leave this page?" prompt would make F5 do nothing, so a
            // page can't use one to stop the user reloading it. The browser's own keys (F5 included) stay on.
            core.ScriptDialogOpening += (_, args) =>
            {
                if (args.Kind == CoreWebView2ScriptDialogKind.Beforeunload)
                {
                    args.Accept();
                }
            };
            core.WebMessageReceived += OnWebMessageReceived;
            // The GPU and utility processes recover on their own and are shared with other plugins' pages.
            core.ProcessFailed += (sender, args) =>
            {
                if (ReferenceEquals(sender, _view?.CoreWebView2) &&
                    args.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited
                    or CoreWebView2ProcessFailedKind.RenderProcessExited
                    or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    ShowStopped();
                }
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(PluginSdk.Build(Manifest));
            if (generation != _generation)
            {
                return;
            }

            _view = view;
            ResetFloodWindow();
            // The panel may have been shown or hidden while the view was starting.
            MoveView(_root.IsVisible ? _viewHost : _parkingHost);
            _stoppedNotice.Visibility = Visibility.Collapsed;
            core.Navigate(new Uri(PluginNetworkPolicy.Origin(Manifest.Id), Manifest.Panel.Replace('\\', '/')).AbsoluteUri);
            started = true;
            RunningChanged?.Invoke();
        }
        catch when (generation != _generation)
        {
            // Stopped while it was starting, so the failure no longer matters.
        }
        catch
        {
            // The panel would otherwise be blank with no way to try again.
            _stoppedNotice.Visibility = Visibility.Visible;
            throw;
        }
        finally
        {
            if (_startingGeneration == generation)
            {
                _startingGeneration = -1;
            }

            if (!started)
            {
                if (ReferenceEquals(_view, view))
                {
                    _view = null;
                }

                Discard(view);
            }
        }
    }

    public void Stop()
    {
        _generation++;
        var view = _view;
        _view = null;
        Discard(view);
        if (view is not null)
        {
            RunningChanged?.Invoke();
        }
    }

    public void PostEvent(string name, object? data)
    {
        try
        {
            _view?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { @event = name, data }, Json));
        }
        catch (Exception)
        {
            // The page went away between the check and the post, or the data can't be written as JSON (a NaN, say).
            // An event that can't be sent must never take the app down.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _http.Dispose();
    }

    private async Task RestartAsync()
    {
        Stop();
        try
        {
            await StartAsync();
        }
        catch
        {
            ShowStopped();
        }
    }

    private void ShowStopped()
    {
        Stop();
        _stoppedNotice.Visibility = Visibility.Visible;
    }

    private static void Discard(WebView2CompositionControl? view)
    {
        if (view is null)
        {
            return;
        }

        (view.Parent as Panel)?.Children.Remove(view);
        view.Dispose();
    }

    // Counts page events (a request or message by default) and message or reply characters, and says whether the page
    // has now gone past a flood limit within the current second.
    private bool IsFlooding(int messageCharacters = 0, int events = 1)
    {
        if (Environment.TickCount64 - _windowStart >= 1000)
        {
            ResetFloodWindow();
        }

        _windowEvents += events;
        _windowCharacters += messageCharacters;
        return _windowEvents > MaximumEventsPerSecond || _windowCharacters > MaximumMessageCharactersPerSecond;
    }

    private void ResetFloodWindow()
    {
        _windowStart = Environment.TickCount64;
        _windowEvents = 0;
        _windowCharacters = 0;
        _windowServedBytes = 0;
    }

    private bool IsOwnOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && PluginNetworkPolicy.IsOwnOrigin(uri, Manifest.Id);

    private void MoveView(Panel target)
    {
        if (_view is not { } view || ReferenceEquals(view.Parent, target))
        {
            return;
        }

        (view.Parent as Panel)?.Children.Remove(view);
        target.Children.Add(view);
    }

    // Serves the plugin's own files, with the content security policy on every response, and refuses any outside
    // request the policy wouldn't allow. Anything unexpected is refused too, never let through.
    private void OnWebResourceRequested(
        object? sender, CoreWebView2Environment environment, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            // Plugins get no workers. A service worker would answer the page's requests itself, out of reach of this
            // handler, and worker requests are raised on every web view in the shared environment, so every plugin's
            // handler refuses them. This comes before the flood count so another page's traffic never counts here;
            // so does a late request from a view that was stopped.
            if (args.RequestedSourceKind != CoreWebView2WebResourceRequestSourceKinds.Document ||
                !ReferenceEquals(sender, _view?.CoreWebView2))
            {
                args.Response = Respond(environment, null, 403, "Blocked");
                return;
            }

            if (IsFlooding())
            {
                args.Response = Respond(environment, null, 403, "Blocked");
                ShowStopped();
                return;
            }

            // The page can put this header on its own requests, so it is refused after the flood count, not before.
            if (args.Request.Headers.Contains("Service-Worker"))
            {
                args.Response = Respond(environment, null, 403, "Blocked");
                return;
            }

            if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri))
            {
                args.Response = Respond(environment, null, 403, "Blocked");
                return;
            }

            // data: and blob: URLs never leave the page; the content security policy decides where they may be used.
            if (uri.Scheme is "data" or "blob")
            {
                return;
            }

            if (PluginNetworkPolicy.IsOwnOrigin(uri, Manifest.Id))
            {
                args.Response = ServeFile(environment, args.Request.Method, uri);
                if (_windowServedBytes > MaximumServedBytesPerSecond)
                {
                    ShowStopped();
                }

                return;
            }

            var isScript = args.ResourceContext == CoreWebView2WebResourceContext.Script;
            if (!PluginNetworkPolicy.IsAllowed(uri, Manifest, _trust, isScript))
            {
                args.Response = Respond(environment, null, 403, "Blocked");
            }
        }
        catch (Exception)
        {
            args.Response = Respond(environment, null, 403, "Blocked");
        }
    }

    private CoreWebView2WebResourceResponse ServeFile(CoreWebView2Environment environment, string method, Uri uri)
    {
        if (method == "GET" && PluginFileServer.TryResolve(Manifest, uri.AbsolutePath, out var path, out var type))
        {
            try
            {
                // A file over the limit falls through to the 404, the same as a missing one.
                var length = new FileInfo(path).Length;
                if (length <= MaximumFileBytes)
                {
                    _windowServedBytes += length;
                    return Respond(environment, new MemoryStream(File.ReadAllBytes(path)), 200, "OK", type);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Fall through to a 404: the file went away or is locked by an editor mid-save.
            }
        }

        return Respond(environment, null, 404, "Not Found");
    }

    // Every response FourFold creates carries the same policy headers, so none can be left without them.
    private CoreWebView2WebResourceResponse Respond(
        CoreWebView2Environment environment, Stream? content, int status, string reason, string? contentType = null) =>
        environment.CreateWebResourceResponse(
            content, status, reason,
            (contentType is null ? string.Empty : $"Content-Type: {contentType}\r\n") +
            $"Content-Security-Policy: {_contentSecurityPolicy}\r\nX-Content-Type-Options: nosniff\r\n" +
            "Cache-Control: no-store");

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            // A late event from a view that was stopped must neither act nor be answered into a newer view.
            if (!ReferenceEquals(sender, _view?.CoreWebView2))
            {
                return;
            }

            var message = args.WebMessageAsJson;
            if (IsFlooding(message.Length))
            {
                ShowStopped();
                return;
            }

            if (!IsOwnOrigin(args.Source))
            {
                return;
            }

            var view = _view;
            var reply = await _api.HandleAsync(message);
            // A reply for a page that was stopped or reloaded while the call ran is dropped.
            if (reply is not null && view is not null && ReferenceEquals(view, _view))
            {
                // The reply's text is counted, but it is not a second event.
                if (IsFlooding(reply.Length, events: 0))
                {
                    ShowStopped();
                    return;
                }

                view.CoreWebView2.PostWebMessageAsJson(reply);
            }
        }
        catch (Exception)
        {
            // A plugin's message must never take the app down.
        }
    }
}
