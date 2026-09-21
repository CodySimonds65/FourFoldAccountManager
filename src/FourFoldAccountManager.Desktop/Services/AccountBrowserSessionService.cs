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
    private readonly FourFoldNavigationPolicy _navigationPolicy = new(["fourfoldonline.com"]);
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Dictionary<Guid, SessionView> _views = [];
    private readonly Dictionary<Guid, GameViewportSize> _gameViewportSizes = [];
    private Task<CoreWebView2Environment>? _environmentTask;
    private bool _fillGameToPanel = true;

    public AccountBrowserSessionService(LocalDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// Raised when a slot attempts to leave the approved FourFold HTTPS origin.
    /// Event data contains only the account ID and block kind; it never exposes
    /// the attempted URL, which could contain sensitive query values.
    /// </summary>
    public event EventHandler<NavigationBlockedEventArgs>? NavigationBlocked;

    public Task<WebView2CompositionControl> CreateViewAsync(
        Guid accountId,
        Panel host,
        CancellationToken cancellationToken = default) =>
        InvokeOnDispatcherAsync(() => CreateViewCoreAsync(accountId, host, cancellationToken), cancellationToken);

    public Task<BrowserNavigationResult> NavigateAndWaitAsync(
        Guid accountId,
        Uri destination,
        CancellationToken cancellationToken = default) =>
        InvokeOnDispatcherAsync(() => NavigateAndWaitCoreAsync(accountId, destination, cancellationToken), cancellationToken);

    public Task<LoginSubmissionResult> SubmitSavedLoginAsync(
        Guid accountId,
        AccountCredentials credentials,
        CancellationToken cancellationToken = default) =>
        InvokeOnDispatcherAsync(() => SubmitSavedLoginCoreAsync(accountId, credentials, cancellationToken), cancellationToken);

    public Task<PlayInBrowserResult> SelectPlayInBrowserAsync(
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        InvokeOnDispatcherAsync(() => SelectPlayInBrowserCoreAsync(accountId, cancellationToken), cancellationToken);

    public Task CloseViewAsync(Guid accountId) =>
        InvokeOnDispatcherAsync(() => CloseViewCoreAsync(accountId), CancellationToken.None);

    public Task SetGameScalingAsync(bool fillGameToPanel, CancellationToken cancellationToken = default) =>
        InvokeOnDispatcherAsync(() => SetGameScalingCoreAsync(fillGameToPanel, cancellationToken), cancellationToken);

    public Task SetGameViewportSizeAsync(
        Guid accountId,
        GameViewportSize size,
        CancellationToken cancellationToken = default) =>
        InvokeOnDispatcherAsync(() => SetGameViewportSizeCoreAsync(accountId, size, cancellationToken), cancellationToken);

    public Task ClearProfileAsync(
        Guid accountId,
        Panel temporaryViewHost,
        CancellationToken cancellationToken = default) =>
        InvokeOnDispatcherAsync(() => ClearProfileCoreAsync(accountId, temporaryViewHost, cancellationToken), cancellationToken);

    private async Task<WebView2CompositionControl> CreateViewCoreAsync(
        Guid accountId,
        Panel host,
        CancellationToken cancellationToken)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(host);
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycleGate.WaitAsync(cancellationToken);
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

            var view = await CreateInitializedViewAsync(accountId, host, cancellationToken);
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
                if (TryApprove(args.Uri, out _))
                {
                    return;
                }

                args.Cancel = true;
                RaiseNavigationBlocked(accountId, NavigationBlockedKind.TopLevelNavigation);
            };

            EventHandler<CoreWebView2NewWindowRequestedEventArgs> newWindowRequested = (_, args) =>
            {
                args.Handled = true;
                if (TryApprove(args.Uri, out var approved))
                {
                    view.CoreWebView2.Navigate(approved.AbsoluteUri);
                    return;
                }

                RaiseNavigationBlocked(accountId, NavigationBlockedKind.Popup);
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

    private async Task SetGameScalingCoreAsync(bool fillGameToPanel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            _fillGameToPanel = fillGameToPanel;
            foreach (var (accountId, session) in _views)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var core = session.View.CoreWebView2;
                var viewportSize = GetGameViewportSize(accountId);
                var replacementScriptId = await InstallGameScalingScriptAsync(
                    core,
                    fillGameToPanel,
                    viewportSize.WidthPercent,
                    viewportSize.HeightPercent);
                core.RemoveScriptToExecuteOnDocumentCreated(session.ScalingScriptId);
                session.ScalingScriptId = replacementScriptId;

                if (TryApprove(core.Source, out _))
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

    private async Task SetGameViewportSizeCoreAsync(
        Guid accountId,
        GameViewportSize size,
        CancellationToken cancellationToken)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(size);
        if (!size.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Viewport dimensions must be between 25% and 100%.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken);
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

    private async Task<BrowserNavigationResult> NavigateAndWaitCoreAsync(
        Guid accountId,
        Uri destination,
        CancellationToken cancellationToken)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_views.TryGetValue(accountId, out var session))
        {
            return BrowserNavigationResult.ViewNotOpen;
        }

        if (!TryApprove(destination, out var approved))
        {
            RaiseNavigationBlocked(accountId, NavigationBlockedKind.RequestedNavigation);
            return BrowserNavigationResult.Blocked;
        }

        var core = session.View.CoreWebView2;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? targetNavigationId = null;
        EventHandler<CoreWebView2NavigationStartingEventArgs> navigationStarting = (_, args) =>
        {
            if (IsSamePage(args.Uri, approved))
            {
                targetNavigationId = args.NavigationId;
            }
        };
        EventHandler<CoreWebView2NavigationCompletedEventArgs> navigationCompleted = (_, args) =>
        {
            if (targetNavigationId == args.NavigationId)
            {
                completion.TrySetResult(args.IsSuccess);
            }
        };

        core.NavigationStarting += navigationStarting;
        core.NavigationCompleted += navigationCompleted;
        try
        {
            core.Navigate(approved.AbsoluteUri);
            try
            {
                var succeeded = await completion.Task.WaitAsync(NavigationTimeout, cancellationToken);
                return succeeded ? BrowserNavigationResult.Navigated : BrowserNavigationResult.Failed;
            }
            catch (TimeoutException)
            {
                return BrowserNavigationResult.TimedOut;
            }
        }
        finally
        {
            core.NavigationStarting -= navigationStarting;
            core.NavigationCompleted -= navigationCompleted;
        }
    }

    private async Task<LoginSubmissionResult> SubmitSavedLoginCoreAsync(
        Guid accountId,
        AccountCredentials credentials,
        CancellationToken cancellationToken)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(credentials);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_views.TryGetValue(accountId, out var session))
        {
            return LoginSubmissionResult.ViewNotOpen;
        }

        var core = session.View.CoreWebView2;
        if (!IsOnLoginPage(core.Source))
        {
            return LoginSubmissionResult.NotOnLoginPage;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? authNavigationId = null;
        EventHandler<CoreWebView2NavigationStartingEventArgs> navigationStarting = (_, args) =>
        {
            if (IsSamePage(args.Uri, FourFoldDestination.LoginSubmitUri))
            {
                authNavigationId = args.NavigationId;
            }
        };
        EventHandler<CoreWebView2NavigationCompletedEventArgs> navigationCompleted = (_, args) =>
        {
            if (authNavigationId == args.NavigationId)
            {
                completion.TrySetResult(args.IsSuccess);
            }
        };

        core.NavigationStarting += navigationStarting;
        core.NavigationCompleted += navigationCompleted;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scriptResult = await SubmitLoginFormScriptAsync(core, credentials);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(scriptResult, "true", StringComparison.OrdinalIgnoreCase))
            {
                return LoginSubmissionResult.LoginFieldsNotFound;
            }

            try
            {
                var succeeded = await completion.Task.WaitAsync(NavigationTimeout, cancellationToken);
                return succeeded ? LoginSubmissionResult.Submitted : LoginSubmissionResult.NavigationFailed;
            }
            catch (TimeoutException)
            {
                return LoginSubmissionResult.TimedOut;
            }
        }
        finally
        {
            core.NavigationStarting -= navigationStarting;
            core.NavigationCompleted -= navigationCompleted;
        }
    }

    private async Task<PlayInBrowserResult> SelectPlayInBrowserCoreAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        ValidateAccountId(accountId);
        cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
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
            if (_views.Remove(accountId, out var session))
            {
                session.View.CoreWebView2.NavigationStarting -= session.NavigationStarting;
                session.View.CoreWebView2.NewWindowRequested -= session.NewWindowRequested;
                DetachFromParent(session.View);
                session.View.Dispose();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task ClearProfileCoreAsync(
        Guid accountId,
        Panel temporaryViewHost,
        CancellationToken cancellationToken)
    {
        ValidateAccountId(accountId);
        ArgumentNullException.ThrowIfNull(temporaryViewHost);
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            CloseViewCoreUnderLock(accountId);

            var temporaryView = await CreateInitializedViewAsync(accountId, temporaryViewHost, cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
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

    private async Task<WebView2CompositionControl> CreateInitializedViewAsync(
        Guid accountId,
        Panel host,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var environment = await GetEnvironmentAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = accountId.ToString("N");
        options.IsInPrivateModeEnabled = false;

        var view = new WebView2CompositionControl();
        try
        {
            host.Children.Add(view);
            await view.EnsureCoreWebView2Async(environment, options);
            cancellationToken.ThrowIfCancellationRequested();
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

    private async Task<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_paths.WebViewUserDataRoot);
        var environmentTask = _environmentTask ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: _paths.WebViewUserDataRoot);
        try
        {
            return await environmentTask.WaitAsync(cancellationToken);
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

    private bool TryApprove(string value, out Uri approved)
    {
        approved = null!;
        return Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            _navigationPolicy.TryValidate(candidate, out approved);
    }

    private bool TryApprove(Uri candidate, out Uri approved) =>
        _navigationPolicy.TryValidate(candidate, out approved);

    private void RaiseNavigationBlocked(Guid accountId, NavigationBlockedKind kind) =>
        NavigationBlocked?.Invoke(this, new NavigationBlockedEventArgs(accountId, kind));

    private async Task<T> InvokeOnDispatcherAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_dispatcher.CheckAccess())
        {
            return await operation();
        }

        var dispatched = _dispatcher.InvokeAsync(operation, DispatcherPriority.Normal, cancellationToken);
        return await dispatched.Task.Unwrap();
    }

    private async Task InvokeOnDispatcherAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        if (_dispatcher.CheckAccess())
        {
            await operation();
            return;
        }

        var dispatched = _dispatcher.InvokeAsync(operation, DispatcherPriority.Normal, cancellationToken);
        await dispatched.Task.Unwrap();
    }

    private static void ValidateAccountId(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }
    }

    private static void DetachFromParent(WebView2CompositionControl view)
    {
        switch (view.Parent)
        {
            case Panel panel:
                panel.Children.Remove(view);
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, view):
                contentControl.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, view):
                decorator.Child = null;
                break;
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
