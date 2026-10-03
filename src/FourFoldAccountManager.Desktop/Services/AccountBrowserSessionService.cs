using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Navigation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FourFoldAccountManager.Desktop.Services;

/// <summary>
/// Owns one durable WebView2 profile per account ID. Create and use the service
/// from the WPF UI thread; its methods marshal WebView2 work to that dispatcher.
/// </summary>
public sealed class AccountBrowserSessionService
{
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);
    private readonly LocalDataPaths _paths;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Dictionary<Guid, SessionView> _views = [];
    private readonly Dictionary<Guid, GameViewportSize> _gameViewportSizes = [];
    private readonly Func<bool> _blockStorePages;
    private Task<CoreWebView2Environment>? _environmentTask;
    private bool _fillGameToPanel = true;

    /// <param name="blockStorePages">
    /// Read on every navigation. When true, the store and gold pages don't load: the game opens them as popups that
    /// would replace it in its panel.
    /// </param>
    public AccountBrowserSessionService(LocalDataPaths paths, Func<bool> blockStorePages)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _blockStorePages = blockStorePages;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// Raised when a slot attempts to leave the approved FourFold HTTPS origin.
    /// Event data contains only the account ID; it never exposes the attempted
    /// URL, which could contain sensitive query values.
    /// </summary>
    public event Action<Guid>? NavigationBlocked;

    /// <summary>Raised when a slot is stopped from opening the store or gold page.</summary>
    public event Action<Guid>? StorePageBlocked;

    public Task<WebView2CompositionControl> CreateViewAsync(Guid accountId, Panel host) =>
        InvokeOnDispatcherAsync(() => CreateViewCoreAsync(accountId, host));

    public Task<BrowserNavigationResult> NavigateAndWaitAsync(Guid accountId, Uri destination) =>
        InvokeOnDispatcherAsync(() => NavigateAndWaitCoreAsync(accountId, destination));

    public Task<LoginSubmissionResult> SubmitSavedLoginAsync(Guid accountId, AccountCredentials credentials) =>
        InvokeOnDispatcherAsync(() => SubmitSavedLoginCoreAsync(accountId, credentials));

    public Task<PlayInBrowserResult> SelectPlayInBrowserAsync(Guid accountId) =>
        InvokeOnDispatcherAsync(() => SelectPlayInBrowserCoreAsync(accountId));

    public Task CloseViewAsync(Guid accountId) =>
        InvokeOnDispatcherAsync(() => CloseViewCoreAsync(accountId));

    public Task SetGameScalingAsync(bool fillGameToPanel) =>
        InvokeOnDispatcherAsync(() => SetGameScalingCoreAsync(fillGameToPanel));

    public Task SetGameViewportSizeAsync(Guid accountId, GameViewportSize size) =>
        InvokeOnDispatcherAsync(() => SetGameViewportSizeCoreAsync(accountId, size));

    public Task ClearProfileAsync(Guid accountId, Panel temporaryViewHost) =>
        InvokeOnDispatcherAsync(() => ClearProfileCoreAsync(accountId, temporaryViewHost));

    private async Task<WebView2CompositionControl> CreateViewCoreAsync(Guid accountId, Panel host)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(host);

        await _lifecycleGate.WaitAsync();
        try
        {
            if (_views.TryGetValue(accountId, out var existing))
            {
                if (!ReferenceEquals(existing.View.Parent, host))
                {
                    DetachFromParent(existing.View);
                    host.Children.Add(existing.View);
                }

                return existing.View;
            }

            var view = await CreateInitializedViewAsync(accountId, host);
            string scalingScriptId;
            try
            {
                var viewportSize = GetGameViewportSize(accountId);
                scalingScriptId = await InstallGameScalingScriptAsync(
                    view.CoreWebView2,
                    _fillGameToPanel,
                    viewportSize.WidthPercent,
                    viewportSize.HeightPercent);
            }
            catch
            {
                DetachFromParent(view);
                view.Dispose();
                throw;
            }
            EventHandler<CoreWebView2NavigationStartingEventArgs> navigationStarting = (sender, args) =>
            {
                if (!IsApproved(args.Uri))
                {
                    args.Cancel = true;
                    RaiseNavigationBlocked(accountId);
                    return;
                }

                // Store popups reach here too: the popup handler below navigates this view to them.
                if (_blockStorePages() && FourFoldNavigationPolicy.IsStorePage(new Uri(args.Uri)))
                {
                    args.Cancel = true;
                    StorePageBlocked?.Invoke(accountId);
                }
            };

            EventHandler<CoreWebView2NewWindowRequestedEventArgs> newWindowRequested = (_, args) =>
            {
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) &&
                    FourFoldNavigationPolicy.IsAllowed(target))
                {
                    view.CoreWebView2.Navigate(target.AbsoluteUri);
                    return;
                }

                RaiseNavigationBlocked(accountId);
            };

            view.CoreWebView2.NavigationStarting += navigationStarting;
            view.CoreWebView2.NewWindowRequested += newWindowRequested;
            _views.Add(accountId, new SessionView(view, navigationStarting, newWindowRequested, scalingScriptId));
            return view;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task SetGameScalingCoreAsync(bool fillGameToPanel)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            _fillGameToPanel = fillGameToPanel;
            foreach (var (accountId, session) in _views)
            {
                var core = session.View.CoreWebView2;
                var viewportSize = GetGameViewportSize(accountId);
                var replacementScriptId = await InstallGameScalingScriptAsync(
                    core,
                    fillGameToPanel,
                    viewportSize.WidthPercent,
                    viewportSize.HeightPercent);
                core.RemoveScriptToExecuteOnDocumentCreated(session.ScalingScriptId);
                session.ScalingScriptId = replacementScriptId;

                if (IsApproved(core.Source))
                {
                    core.Reload();
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task SetGameViewportSizeCoreAsync(Guid accountId, GameViewportSize size)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(size);
        if (!size.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Viewport dimensions must be between 25% and 100%.");
        }

        await _lifecycleGate.WaitAsync();
        try
        {
            if (!_views.TryGetValue(accountId, out var session))
            {
                _gameViewportSizes[accountId] = size;
                return;
            }

            var core = session.View.CoreWebView2;
            var replacementScriptId = await InstallGameScalingScriptAsync(
                core,
                _fillGameToPanel,
                size.WidthPercent,
                size.HeightPercent);
            try
            {
                await core.ExecuteScriptAsync(BuildGameScalingScript(
                    _fillGameToPanel,
                    size.WidthPercent,
                    size.HeightPercent));
            }
            catch
            {
                core.RemoveScriptToExecuteOnDocumentCreated(replacementScriptId);
                throw;
            }

            core.RemoveScriptToExecuteOnDocumentCreated(session.ScalingScriptId);
            session.ScalingScriptId = replacementScriptId;
            _gameViewportSizes[accountId] = size;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static Task<string> InstallGameScalingScriptAsync(
        CoreWebView2 core,
        bool fillGameToPanel,
        double widthPercent,
        double heightPercent) =>
        core.AddScriptToExecuteOnDocumentCreatedAsync(
            BuildGameScalingScript(fillGameToPanel, widthPercent, heightPercent));

    private static string BuildGameScalingScript(
        bool fillGameToPanel,
        double widthPercent,
        double heightPercent)
    {
        var fillStyle = fillGameToPanel
            ? $$"""
              #unity-container {
                width: {{widthPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)}}vw !important;
                height: {{heightPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)}}vh !important;
                max-width: none !important;
                max-height: none !important;
              }
              #unity-canvas {
                width: 100% !important;
                height: 100% !important;
              }
              """
            : string.Empty;
        var serializedStyle = JsonSerializer.Serialize(fillStyle);
        var script = $$"""
            (() => {
                if (location.protocol !== 'https:' || location.hostname !== 'fourfoldonline.com') return;
                const style = document.createElement('style');
                style.id = 'fourfold-account-manager-scaling';
                style.textContent = {{serializedStyle}};
                const install = () => {
                    const target = document.head || document.documentElement;
                    if (!target) return;
                    const existing = document.getElementById(style.id);
                    if (existing) existing.textContent = style.textContent;
                    else target.appendChild(style);
                };
                if (document.head || document.documentElement) install();
                else document.addEventListener('DOMContentLoaded', install, { once: true });
            })();
            """;
        return script;
    }

    private GameViewportSize GetGameViewportSize(Guid accountId) =>
        _gameViewportSizes.TryGetValue(accountId, out var size)
            ? size
            : GameViewportSize.Default;

    private async Task<BrowserNavigationResult> NavigateAndWaitCoreAsync(Guid accountId, Uri destination)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(destination);

        if (!_views.TryGetValue(accountId, out var session))
        {
            return BrowserNavigationResult.ViewNotOpen;
        }

        if (!FourFoldNavigationPolicy.IsAllowed(destination))
        {
            RaiseNavigationBlocked(accountId);
            return BrowserNavigationResult.Blocked;
        }

        var core = session.View.CoreWebView2;
        using var watch = new NavigationWatch(core, destination);
        core.Navigate(destination.AbsoluteUri);
        return await watch.WaitAsync() switch
        {
            true => BrowserNavigationResult.Navigated,
            false => BrowserNavigationResult.Failed,
            null => BrowserNavigationResult.TimedOut
        };
    }

    private async Task<LoginSubmissionResult> SubmitSavedLoginCoreAsync(Guid accountId, AccountCredentials credentials)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(credentials);

        if (!_views.TryGetValue(accountId, out var session))
        {
            return LoginSubmissionResult.ViewNotOpen;
        }

        var core = session.View.CoreWebView2;
        if (!IsOnLoginPage(core.Source))
        {
            return LoginSubmissionResult.NotOnLoginPage;
        }

        using var watch = new NavigationWatch(core, FourFoldDestination.LoginSubmitUri);
        var scriptResult = await SubmitLoginFormScriptAsync(core, credentials);
        if (!string.Equals(scriptResult, "true", StringComparison.OrdinalIgnoreCase))
        {
            return LoginSubmissionResult.LoginFieldsNotFound;
        }

        return await watch.WaitAsync() switch
        {
            true => LoginSubmissionResult.Submitted,
            false => LoginSubmissionResult.NavigationFailed,
            null => LoginSubmissionResult.TimedOut
        };
    }

    private async Task<PlayInBrowserResult> SelectPlayInBrowserCoreAsync(Guid accountId)
    {
        ValidateAccountId(accountId);
        if (!_views.TryGetValue(accountId, out var session))
        {
            return PlayInBrowserResult.ViewNotOpen;
        }

        var core = session.View.CoreWebView2;
        if (!IsSamePage(core.Source, FourFoldDestination.StartUri))
        {
            return PlayInBrowserResult.NotOnPlayPage;
        }

        var scriptResult = await SelectPlayInBrowserScriptAsync(core);
        return string.Equals(scriptResult, "true", StringComparison.OrdinalIgnoreCase)
            ? PlayInBrowserResult.Activated
            : PlayInBrowserResult.ControlNotFound;
    }

    private static Task<string> SelectPlayInBrowserScriptAsync(CoreWebView2 core)
    {
        const string script = """
            (() => {
                const normalize = value => (value || '').replace(/\s+/g, ' ').trim().toLowerCase();
                const heading = Array.from(document.querySelectorAll('h1, h2, h3, h4, h5, h6'))
                    .find(candidate => normalize(candidate.textContent) === 'play in browser');
                if (!heading) return false;

                let scope = heading.parentElement;
                while (scope && scope !== document.body) {
                    const action = Array.from(scope.querySelectorAll(
                        'a, button, input[type="button"], input[type="submit"]'))
                        .find(candidate => {
                            const label = candidate instanceof HTMLInputElement
                                ? candidate.value
                                : candidate.textContent;
                            return normalize(label) === 'play now' &&
                                !candidate.disabled &&
                                candidate.getAttribute('aria-disabled') !== 'true' &&
                                candidate.getClientRects().length > 0;
                        });
                    if (action) {
                        if (action instanceof HTMLAnchorElement) {
                            const destination = new URL(action.href, location.href);
                            if (destination.origin !== location.origin) return false;
                        }
                        if (action instanceof HTMLButtonElement && action.form) {
                            const destination = new URL(action.form.action || location.href, location.href);
                            if (destination.origin !== location.origin) return false;
                        }
                        action.click();
                        return true;
                    }
                    scope = scope.parentElement;
                }
                return false;
            })()
            """;

        return core.ExecuteScriptAsync(script);
    }

    private static Task<string> SubmitLoginFormScriptAsync(
        CoreWebView2 core,
        AccountCredentials credentials)
    {
        var username = JsonSerializer.Serialize(credentials.Username);
        var password = JsonSerializer.Serialize(credentials.Password);
        var script = $$"""
            (() => {
                const form = Array.from(document.forms).find(candidate => {
                    const action = new URL(candidate.action, location.href);
                    return candidate.method.toLowerCase() === 'post' &&
                        action.origin === location.origin && action.pathname === '/auth.php';
                });
                const username = form?.querySelector('input[name="username"]');
                const password = form?.querySelector('input[name="password"][type="password"]');
                if (!form || !username || !password || username.type !== 'text') return false;
                const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
                if (!setter) return false;
                setter.call(username, {{username}});
                username.dispatchEvent(new Event('input', { bubbles: true }));
                username.dispatchEvent(new Event('change', { bubbles: true }));
                setter.call(password, {{password}});
                password.dispatchEvent(new Event('input', { bubbles: true }));
                password.dispatchEvent(new Event('change', { bubbles: true }));
                form.requestSubmit();
                return true;
            })()
            """;

        return core.ExecuteScriptAsync(script);
    }

    private static bool IsOnLoginPage(string source) => IsSamePage(source, FourFoldDestination.LoginUri);

    private static bool IsSamePage(string value, Uri expected) =>
        Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
        string.Equals(candidate.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        candidate.Port == expected.Port &&
        string.Equals(candidate.AbsolutePath.TrimEnd('/'), expected.AbsolutePath.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private async Task CloseViewCoreAsync(Guid accountId)
    {
        ValidateAccountId(accountId);
        await _lifecycleGate.WaitAsync();
        try
        {
            CloseViewCoreUnderLock(accountId);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task ClearProfileCoreAsync(Guid accountId, Panel temporaryViewHost)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(temporaryViewHost);

        await _lifecycleGate.WaitAsync();
        try
        {
            CloseViewCoreUnderLock(accountId);

            var temporaryView = await CreateInitializedViewAsync(accountId, temporaryViewHost);
            try
            {
                await temporaryView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.AllProfile);
            }
            finally
            {
                DetachFromParent(temporaryView);
                temporaryView.Dispose();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void CloseViewCoreUnderLock(Guid accountId)
    {
        if (!_views.Remove(accountId, out var session))
        {
            return;
        }

        session.View.CoreWebView2.NavigationStarting -= session.NavigationStarting;
        session.View.CoreWebView2.NewWindowRequested -= session.NewWindowRequested;
        DetachFromParent(session.View);
        session.View.Dispose();
    }

    private async Task<WebView2CompositionControl> CreateInitializedViewAsync(Guid accountId, Panel host)
    {
        var environment = await GetEnvironmentAsync();

        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = accountId.ToString("N");
        options.IsInPrivateModeEnabled = false;

        var view = new WebView2CompositionControl();
        try
        {
            host.Children.Add(view);
            await view.EnsureCoreWebView2Async(environment, options);
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            return view;
        }
        catch
        {
            DetachFromParent(view);
            view.Dispose();
            throw;
        }
    }

    private async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        Directory.CreateDirectory(_paths.WebViewUserDataRoot);
        var environmentTask = _environmentTask ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: _paths.WebViewUserDataRoot);
        try
        {
            return await environmentTask;
        }
        catch
        {
            if (environmentTask.IsFaulted || environmentTask.IsCanceled)
            {
                _environmentTask = null;
            }

            throw;
        }
    }

    private static bool IsApproved(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var candidate) && FourFoldNavigationPolicy.IsAllowed(candidate);

    private void RaiseNavigationBlocked(Guid accountId) => NavigationBlocked?.Invoke(accountId);

    private async Task<T> InvokeOnDispatcherAsync<T>(Func<Task<T>> operation)
    {
        if (_dispatcher.CheckAccess())
        {
            return await operation();
        }

        var dispatched = _dispatcher.InvokeAsync(operation);
        return await dispatched.Task.Unwrap();
    }

    private async Task InvokeOnDispatcherAsync(Func<Task> operation)
    {
        if (_dispatcher.CheckAccess())
        {
            await operation();
            return;
        }

        var dispatched = _dispatcher.InvokeAsync(operation);
        await dispatched.Task.Unwrap();
    }

    private static void ValidateAccountId(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }
    }

    // Views are only ever hosted in a Panel.
    private static void DetachFromParent(WebView2CompositionControl view) =>
        (view.Parent as Panel)?.Children.Remove(view);

    // Matches one navigation to `page` by its ID. Create it before starting the navigation so no event is
    // missed, and dispose it to unsubscribe.
    private sealed class NavigationWatch : IDisposable
    {
        private readonly CoreWebView2 _core;
        private readonly Uri _page;
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ulong? _navigationId;

        public NavigationWatch(CoreWebView2 core, Uri page)
        {
            _core = core;
            _page = page;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
        }

        // Whether the navigation succeeded, or null if it did not finish within the timeout.
        public async Task<bool?> WaitAsync()
        {
            try
            {
                return await _completion.Task.WaitAsync(NavigationTimeout);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            _core.NavigationStarting -= OnNavigationStarting;
            _core.NavigationCompleted -= OnNavigationCompleted;
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (IsSamePage(args.Uri, _page))
            {
                _navigationId = args.NavigationId;
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_navigationId == args.NavigationId)
            {
                _completion.TrySetResult(args.IsSuccess);
            }
        }
    }

    private sealed class SessionView(
        WebView2CompositionControl view,
        EventHandler<CoreWebView2NavigationStartingEventArgs> navigationStarting,
        EventHandler<CoreWebView2NewWindowRequestedEventArgs> newWindowRequested,
        string scalingScriptId)
    {
        public WebView2CompositionControl View { get; } = view;
        public EventHandler<CoreWebView2NavigationStartingEventArgs> NavigationStarting { get; } = navigationStarting;
        public EventHandler<CoreWebView2NewWindowRequestedEventArgs> NewWindowRequested { get; } = newWindowRequested;
        public string ScalingScriptId { get; set; } = scalingScriptId;
    }
}
