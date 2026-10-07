using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Plugins;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FourFoldAccountManager.Desktop.Plugins.Web;

// A community plugin: a sandboxed page in the shared plugin browser. The page keeps running while its panel isn't
// showing (its web view waits in a small invisible parking host), so its cards stay up to date.
public sealed class WebPlugin : IFourFoldPlugin, IDisposable
{
    // A page can post messages or ask for files in a tight loop, faster than the UI thread can answer, which freezes
    // the whole app (and again on every launch). More than this many events, or this much message text, within one
    // second stops the plugin; its Reload button starts it fresh. Replies have a budget of their own, because one web
    // request's reply can be several MB once JSON-escaped (non-ASCII text grows up to six-fold), but a loop of reads that
    // each return a big stored value still saturates the UI thread well before the event limit.
    private const int MaximumEventsPerSecond = 2_000;
    private const int MaximumMessageCharactersPerSecond = 8 * 1024 * 1024;
    private const int MaximumReplyCharactersPerSecond = 64 * 1024 * 1024;

    // Serving a file reads it whole on the UI thread, so a larger one is treated as missing, and a page that asks for
    // more than this much of its own files within one second is stopped like any other flood.
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private const long MaximumServedBytesPerSecond = 32 * 1024 * 1024;

    // Without a cap, IndexedDB, the Cache API and the origin-private file system get a share of the disk (10 GB in tests).
    private const int MaximumBrowserStorageBytes = 5 * 1024 * 1024;

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

    // How many navigations the page has begun. A reply is for the page that made the call, and a new page's call ids
    // start again at 1, so a reply that finishes after a navigation is dropped.
    private int _navigations;

    // The current one-second window of the flood limits.
    private long _windowStart;
    private int _windowEvents;
    private long _windowCharacters;
    private long _windowReplyCharacters;
    private long _windowServedBytes;

    public WebPlugin(
        PluginManifest manifest,
        PluginTrust trust,
        PluginBrowser browser,
        Panel parkingHost,
        IPluginHostData host,
        PluginCardStore cards,
        PluginStorage storage)
    {
        Manifest = manifest;
        _trust = trust;
        _browser = browser;
        _parkingHost = parkingHost;
        _http = new PluginHttpFetcher(manifest, trust);
        _api = new PluginApi(manifest, trust, host, storage, _http, cards, TimeProvider.System);
        _contentSecurityPolicy = PluginNetworkPolicy.BuildContentSecurityPolicy(manifest, trust);
        // A plugin with no icon image shows the default Segoe glyph, a puzzle piece.
        Descriptor = new PluginDescriptor(manifest.Id, manifest.Name, manifest.ShortLabel, "\uEA86", [], [])
        {
            Cards = manifest.Cards.Select(card => new PluginCardDescriptor(card.Id, card.Name, card.Scope)).ToArray(),
            IconPath = manifest.Icon is null ? null : Path.Combine(manifest.Folder, manifest.Icon),
            Badge = trust == PluginTrust.Developer ? "DEV" : null,
            Detail = $"by {manifest.Author}\n" +
                     (PluginNetworkPolicy.AllowsAnySite(manifest, trust) ? "Can contact: any website"
                         : manifest.Sites.Count == 0 ? "Can contact: no websites"
                         : "Can contact: " + string.Join(", ", manifest.Sites.Select(PluginNetworkPolicy.SiteLabel)))
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
        // Everything in the page below is drawn by the plugin, so this bar, drawn by FourFold, says whose page it is.
        var barName = new TextBlock
        {
            Text = manifest.Name,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            MaxWidth = 100,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var barAuthor = new TextBlock
        {
            Text = "by " + manifest.Author,
            FontSize = 11,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        barAuthor.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var barTagText = new TextBlock
        {
            Text = trust == PluginTrust.Developer ? "DEV" : "COMMUNITY",
            FontSize = 9,
            FontWeight = FontWeights.Bold
        };
        barTagText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var barTag = new Border
        {
            Child = barTagText,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        barTag.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderStrong");
        DockPanel.SetDock(barTag, Dock.Right);
        DockPanel.SetDock(barName, Dock.Left);
        var bar = new Border
        {
            Height = 24,
            Padding = new Thickness(9, 0, 9, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            // The tag stays first, so it is measured first and can never be squeezed out.
            Child = new DockPanel { Children = { barTag, barName, barAuthor } }
        };
        bar.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceRaised");
        bar.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(bar);
        Grid.SetRow(_viewHost, 1);
        layout.Children.Add(_viewHost);
        _root = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = layout };
        _root.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        _root.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        // The view lives in the panel while the panel is on screen, and in the parking host the rest of the time. A web
        // view in a hidden tree is throttled by the browser, which would stall the page.
        _root.IsVisibleChanged += (_, _) =>
        {
            try
            {
                MoveView(_root.IsVisible ? _viewHost : _parkingHost);
            }
            catch (Exception)
            {
                // Showing the view builds graphics devices, which fails while the graphics driver is restarting.
                // The plugin stops with its Reload button, instead of the exception ending the app.
                ShowStopped();
            }
        };
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
            UnhookWindowClose(view, core);
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
            core.WebResourceRequested += (_, args) => OnWebResourceRequested(view, environment, args);
            core.NavigationStarting += (_, args) =>
            {
                args.Cancel = !IsOwnOrigin(args.Uri);
                if (!args.Cancel)
                {
                    _navigations++;
                }
            };
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
            core.WebMessageReceived += (_, args) => OnWebMessageReceived(view, args);
            // The GPU and utility processes recover on their own and are shared with other plugins' pages. Controls are
            // compared, not their CoreWebView2: once the browser process has died the control throws from that getter.
            core.ProcessFailed += (_, args) =>
            {
                if (ReferenceEquals(view, _view) &&
                    args.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited
                    or CoreWebView2ProcessFailedKind.RenderProcessExited
                    or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    ShowStopped();
                }
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(PluginSdk.Build(Manifest));
            var origin = PluginNetworkPolicy.Origin(Manifest.Id);
            await core.CallDevToolsProtocolMethodAsync(
                "Storage.overrideQuotaForOrigin",
                JsonSerializer.Serialize(
                    new { origin = origin.GetLeftPart(UriPartial.Authority), quotaSize = MaximumBrowserStorageBytes }, Json));
            if (generation != _generation)
            {
                return;
            }

            _view = view;
            ResetFloodWindow();
            // The panel may have been shown or hidden while the view was starting.
            MoveView(_root.IsVisible ? _viewHost : _parkingHost);
            _stoppedNotice.Visibility = Visibility.Collapsed;
            core.Navigate(new Uri(origin, Manifest.Panel).AbsoluteUri);
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

    // The WPF web view closes its own parent window when a page calls window.close(), before any handler of ours runs,
    // so a plugin could quit FourFold with one line. This removes that handler; a close request from a plugin page is
    // then ignored. It reaches into the control's private members, so re-check it on every WebView2 SDK update: when they
    // are renamed it throws, the plugin doesn't start, and the lost lock is noticed at once.
    private static void UnhookWindowClose(WebView2CompositionControl view, CoreWebView2 core)
    {
        const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(WebView2CompositionControl).GetField("m_webview2Base", NonPublic);
        var handler = field?.FieldType.GetMethod("CoreWebView2_WindowCloseRequested", NonPublic);
        if (field?.GetValue(view) is not { } webViewBase || handler is null)
        {
            throw new InvalidOperationException("The WebView2 control's window.close() handler wasn't found.");
        }

        core.WindowCloseRequested -= handler.CreateDelegate<EventHandler<object>>(webViewBase);
    }

    // Counts one page event (a request, or a message of the given length) and says whether the page has now gone past
    // a flood limit within the current second.
    private bool IsFlooding(int messageCharacters = 0)
    {
        if (Environment.TickCount64 - _windowStart >= 1000)
        {
            ResetFloodWindow();
        }

        _windowEvents++;
        _windowCharacters += messageCharacters;
        return _windowEvents > MaximumEventsPerSecond || _windowCharacters > MaximumMessageCharactersPerSecond;
    }

    // Counts the text of one reply against the reply budget of the current second. A reply is not a page event.
    private bool IsReplyFlooding(int replyCharacters)
    {
        if (Environment.TickCount64 - _windowStart >= 1000)
        {
            ResetFloodWindow();
        }

        _windowReplyCharacters += replyCharacters;
        return _windowReplyCharacters > MaximumReplyCharactersPerSecond;
    }

    private void ResetFloodWindow()
    {
        _windowStart = Environment.TickCount64;
        _windowEvents = 0;
        _windowCharacters = 0;
        _windowReplyCharacters = 0;
        _windowServedBytes = 0;
    }

    private bool IsOwnOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && PluginNetworkPolicy.IsOwnOrigin(uri, Manifest.Id);

    private void MoveView(Panel target)
    {
        if (_view is not { } view)
        {
            return;
        }

        if (!ReferenceEquals(view.Parent, target))
        {
            (view.Parent as Panel)?.Children.Remove(view);
            target.Children.Add(view);
        }

        // After the move: the control finds its window through the tree it is in.
        SetFrameCapture(view, !ReferenceEquals(target, _parkingHost));
    }

    // The WPF web view shows its page by capturing every frame into a Direct3D image of its own: two graphics devices
    // and a capture session for each view, whatever its size. In this process that is about 80 MB, 66 threads and 1,700
    // handles a view, and nobody looks at a parked page, so a parked view gives its image up and builds it again when
    // its panel is shown (about 70 ms). The page itself runs on unchanged. Like UnhookWindowClose, this reaches into the
    // control's private members, so re-check it on every WebView2 SDK update: when they are renamed it throws, the
    // plugin doesn't start, and the lost saving is noticed at once.
    private static void SetFrameCapture(WebView2CompositionControl view, bool capture)
    {
        const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        var imageField = typeof(WebView2CompositionControl).GetField("_d3dImage", NonPublic);
        var buildImage = typeof(WebView2CompositionControl).GetMethod("TryInitializeD3DImage", NonPublic, Type.EmptyTypes);
        var baseField = typeof(WebView2CompositionControl).GetField("m_webview2Base", NonPublic);
        var hostWindowField = baseField?.FieldType.GetField("_hwndTaskSource", NonPublic);
        if (imageField is null || buildImage is null || baseField is null ||
            hostWindowField?.FieldType != typeof(TaskCompletionSource<IntPtr>))
        {
            throw new InvalidOperationException("The WebView2 control's frame image members weren't found.");
        }

        if (capture)
        {
            // Does nothing when the view still has its image.
            buildImage.Invoke(view, null);
            return;
        }

        if (imageField.GetValue(view) is not IDisposable image)
        {
            return;
        }

        // The control completes this source each time it builds its image, and completing one twice throws. Nothing
        // waits on it once the view has started, so a fresh one lets the image be built again.
        hostWindowField.SetValue(baseField.GetValue(view), new TaskCompletionSource<IntPtr>());
        imageField.SetValue(view, null);
        image.Dispose();
        // Disposing isn't enough: the devices and their threads stay until the collector has let go of the image's
        // capture objects, which it has no reason to do soon, because it can't see how much they hold.
        GC.Collect();
    }

    // Serves the plugin's own files, with the content security policy on every response, and refuses any outside
    // request the policy wouldn't allow. Anything unexpected is refused too, never let through.
    private void OnWebResourceRequested(
        WebView2CompositionControl? source, CoreWebView2Environment environment,
        CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            // Plugins get no workers. A service worker would answer the page's requests itself, out of reach of this
            // handler, and worker requests are raised on every web view in the shared environment, so every plugin's
            // handler refuses them. This comes before the flood count so another page's traffic never counts here;
            // so does a late request from a view that was stopped.
            if (args.RequestedSourceKind != CoreWebView2WebResourceRequestSourceKinds.Document ||
                !ReferenceEquals(source, _view))
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

    private async void OnWebMessageReceived(WebView2CompositionControl? source, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            // A late event from a view that was stopped must neither act nor be answered into a newer view.
            if (!ReferenceEquals(source, _view))
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
            var navigations = _navigations;
            var reply = await _api.HandleAsync(message);
            // A reply for a page that was stopped, reloaded or navigated while the call ran is dropped.
            if (reply is not null && view is not null && ReferenceEquals(view, _view) && navigations == _navigations)
            {
                if (IsReplyFlooding(reply.Length))
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
