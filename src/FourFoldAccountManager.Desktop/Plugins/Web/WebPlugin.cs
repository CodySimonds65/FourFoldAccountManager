using System.IO;
using System.Runtime.InteropServices;
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
        // The view lives in the panel while the panel is in a window, and in the parking host the rest of the time.
        _root.Loaded += (_, _) => MoveView(_viewHost);
        _root.Unloaded += (_, _) => MoveView(_parkingHost);
    }

    public PluginManifest Manifest { get; }

    public PluginDescriptor Descriptor { get; }

    public FrameworkElement Panel => _root;

    public FrameworkElement? SettingsPage => null;

    public bool IsRunning => _view is not null;

    public void Opened()
    {
    }

    public async Task StartAsync()
    {
        if (_view is not null || _startingGeneration == _generation)
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

            view = new WebView2CompositionControl();
            (_root.IsLoaded ? _viewHost : _parkingHost).Children.Add(view);
            await view.EnsureCoreWebView2Async(environment);
            if (generation != _generation)
            {
                return;
            }

            var core = view.CoreWebView2;
            var developer = _trust == PluginTrust.Developer;
            core.Settings.AreDevToolsEnabled = developer;
            core.Settings.AreDefaultContextMenusEnabled = developer;
            core.Settings.AreBrowserAcceleratorKeysEnabled = developer;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            // No alert, confirm or prompt: a plugin can't pose as FourFold.
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.AddWebResourceRequestedFilter(
                "*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, args) => OnWebResourceRequested(environment, args);
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
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += (_, _) => ShowStopped();
            await core.AddScriptToExecuteOnDocumentCreatedAsync(PluginSdk.Build(Manifest));
            if (generation != _generation)
            {
                return;
            }

            _view = view;
            // The panel may have been shown or hidden while the view was starting.
            MoveView(_root.IsLoaded ? _viewHost : _parkingHost);
            _stoppedNotice.Visibility = Visibility.Collapsed;
            core.Navigate(new Uri(PluginNetworkPolicy.Origin(Manifest.Id), Manifest.Panel.Replace('\\', '/')).AbsoluteUri);
            started = true;
        }
        catch when (generation != _generation)
        {
            // Stopped while it was starting, so the failure no longer matters.
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
    }

    public void PostEvent(string name, object? data)
    {
        try
        {
            _view?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { @event = name, data }, Json));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException or COMException)
        {
            // The page went away between the check and the post.
        }
    }

    public void Dispose()
    {
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
    private void OnWebResourceRequested(CoreWebView2Environment environment, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
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
                return Respond(environment, new MemoryStream(File.ReadAllBytes(path)), 200, "OK", type);
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
            if (!IsOwnOrigin(args.Source))
            {
                return;
            }

            var view = _view;
            var reply = await _api.HandleAsync(args.WebMessageAsJson);
            // A reply for a page that was stopped or reloaded while the call ran is dropped.
            if (reply is not null && view is not null && ReferenceEquals(view, _view))
            {
                view.CoreWebView2.PostWebMessageAsJson(reply);
            }
        }
        catch (Exception)
        {
            // A plugin's message must never take the app down.
        }
    }
}
