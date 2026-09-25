using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Launch;
using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Panel;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Updates;
using FourFoldAccountManager.Desktop.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FourFoldAccountManager.Desktop;

public partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;

    private readonly ObservableCollection<AccountProfile> _accounts = [];
    private readonly HashSet<Guid> _openAccountIds = [];
    private readonly HashSet<Guid> _failedAccountIds = [];
    private readonly AccountStore _accountStore;
    private readonly LocalDataPaths _dataPaths;
    private readonly SettingsStore _settingsStore;
    private readonly WindowsCredentialStore _credentialStore;
    private readonly AccountBrowserSessionService _browserSessions;
    private readonly XpTrackerCoordinator _xpTracker;
    private LeaderboardCoordinator? _leaderboard;
    private readonly PlayerProfileService _playerProfileService;
    private readonly ObservableCollection<XpTrackerRow> _xpTrackerRows = [];
    private readonly List<PanelSlotCard> _slotCards = [];
    private readonly LayoutDividerResizeController _layoutDividerResizeController = new();
    private readonly SemaphoreSlim _settingsMutationGate = new(1, 1);
    private readonly SemaphoreSlim _viewportSaveGate = new(1, 1);
    private readonly DispatcherTimer _leaderboardRefreshTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private HwndSource? _windowSource;
    private GlobalShortcutRegistry? _shortcuts;
    private PanelSettings _panelSettings = PanelSettings.Default;
    private bool _isReady;
    private bool _batchLaunchInProgress;
    private bool _accountsPanelVisible = true;
    private bool _slotManagementVisible = true;
    private bool _viewAdjustmentVisible;
    private bool _isFullScreen;
    private bool _showingLeaderboard;
    private bool _overlayEditing;
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private bool _shutdownStarted;
    private bool _allowClose;
    private CancellationTokenSource? _profileReadCancellation;
    private long _profileReadGeneration;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;

        var paths = new LocalDataPaths();
        _dataPaths = paths;
        _accountStore = new AccountStore(paths);
        _settingsStore = new SettingsStore(paths);
        _credentialStore = new WindowsCredentialStore();
        _browserSessions = new AccountBrowserSessionService(paths);
        _xpTracker = new XpTrackerCoordinator(paths);
        _playerProfileService = _xpTracker.ProfileService;
        _xpTracker.Changed += (_, _) =>
        {
            RefreshTrackerRows();
            _ = SyncLeaderboardParticipationAsync();
        };
        FullscreenOverlayTray.EditRequested += (_, _) => SetOverlayEditing(true);
        FullscreenOverlayTray.DoneRequested += (_, _) => SetOverlayEditing(false);
        FullscreenOverlayTray.CardToggleRequested += FullscreenOverlayTray_CardToggleRequested;
        GlobalOverlayLayer.BoundsCommitted += OverlayLayer_BoundsCommitted;
        PluginSidebar.SetTrackerItemsSource(_xpTrackerRows);
        PluginSidebar.SetAccounts(_accounts);
        PluginSidebar.LinkRequested += accountId =>
        {
            AccountsListBox.SelectedItem = _accounts.FirstOrDefault(account => account.Id == accountId);
            RenameAccount_Click(PluginSidebar, new RoutedEventArgs());
        };
        PluginSidebar.ResetRateRequested += accountId =>
        {
            _xpTracker.ResetRate(accountId);
            RefreshTrackerRows();
        };
        PluginSidebar.ResetAllRequested += accountId =>
        {
            _xpTracker.ResetAll(accountId);
            RefreshTrackerRows();
        };
        PluginSidebar.RefreshRequested += (_, _) => _ = RefreshSelectedProfileAsync();
        PluginSidebar.AccountSelectionRequested += accountId =>
        {
            var account = _accounts.FirstOrDefault(candidate => candidate.Id == accountId);
            if (account is not null && SelectedAccount?.Id != account.Id)
            {
                AccountsListBox.SelectedItem = account;
            }
        };
        _browserSessions.NavigationBlocked += BrowserSessions_NavigationBlocked;

        AccountsListBox.ItemsSource = _accounts;
        AddAccountButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        LayoutPicker.IsEnabled = false;
        LaunchVisibleButton.IsEnabled = false;
        LayoutPicker.ItemsSource = new[]
        {
            new LayoutChoice(PanelLayout.OneByTwo, "1 × 2 · Side by side"),
            new LayoutChoice(PanelLayout.TwoByOne, "2 × 1 · Stacked"),
            new LayoutChoice(PanelLayout.TwoByTwo, "2 × 2 · Grid"),
            new LayoutChoice(PanelLayout.TwoByThree, "2 × 3 · Five clients"),
            new LayoutChoice(PanelLayout.OneByThree, "1 × 3 · One above three"),
            new LayoutChoice(PanelLayout.OneByTwoVertical, "1 × 2 · Vertical split"),
            new LayoutChoice(PanelLayout.OneByOne, "1 × 1 · Single client")
        };

        LayoutPicker.SelectedValuePath = nameof(LayoutChoice.Layout);
        _leaderboardRefreshTimer.Tick += (_, _) => _ = LeaderboardPanelView.RefreshAsync();
        UpdateTopLevelButtons();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        WindowAppearance.Apply(this);
        var windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(windowHandle);
        if (_windowSource is null)
        {
            return;
        }

        _windowSource.AddHook(MainWindow_HwndSourceHook);
        _shortcuts = new GlobalShortcutRegistry(new WindowsGlobalHotkeyRegistrar(windowHandle));
    }

    private IntPtr MainWindow_HwndSourceHook(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotkey &&
            _shortcuts is { } shortcuts &&
            shortcuts.TryResolve(unchecked((int)wParam.ToInt64()), _panelSettings, out var action) &&
            HandleGlobalShortcut(action))
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private bool HandleGlobalShortcut(GlobalShortcutAction action)
    {
        switch (action)
        {
            case GlobalShortcutAction.RevealOverlays:
                FullscreenOverlayTray.RevealEdgeTab();
                return true;
            case GlobalShortcutAction.ToggleDividerResizing:
                ToggleLayoutDividerResizing();
                return true;
            default:
                return false;
        }
    }

    private AccountProfile? SelectedAccount => AccountsListBox.SelectedItem as AccountProfile;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var account in await _accountStore.LoadAsync())
            {
                _accounts.Add(account);
            }

            _panelSettings = await _settingsStore.LoadAsync();
            try
            {
                var apiOptions = LeaderboardApiOptions.FromEnvironment();
                var apiClient = apiOptions is null ? null :
                    new LeaderboardApiClient(new HttpClient(), apiOptions, ownsClient: true);
                _leaderboard = new LeaderboardCoordinator(new LeaderboardClientStateStore(_dataPaths),
                    apiClient, _panelSettings.ShareLinkedAccounts,
                    async (enabled, _) =>
                    {
                        await UpdateSettingsAsync(settings => settings with { ShareLinkedAccounts = enabled });
                    });
                await _leaderboard.InitializeAsync();
                _ = SyncLeaderboardParticipationAsync();
            }
            catch
            {
                // An unavailable leaderboard must not prevent local account management.
                _leaderboard = null;
            }
            LeaderboardPanelView.Configure(_leaderboard, enabled => SetLeaderboardSharingAsync(enabled));
            _shortcuts?.Initialize(_panelSettings);
            foreach (var (accountId, size) in _panelSettings.GameViewportSizes)
            {
                await _browserSessions.SetGameViewportSizeAsync(accountId, size);
            }
            await _browserSessions.SetGameScalingAsync(_panelSettings.FillGameToPanel);
            _isReady = true;
            AddAccountButton.IsEnabled = true;
            SettingsButton.IsEnabled = true;
            LayoutPicker.IsEnabled = true;
            LaunchVisibleButton.IsEnabled = true;
            LayoutPicker.SelectedValue = _panelSettings.Layout;
            AccountsListBox.SelectedIndex = _accounts.Count > 0 ? 0 : -1;
            UpdateAccountActions();
            await RebuildPanelAsync(closeExistingViews: false);
            GlobalStatusText.Text = "Choose your layout, assign your accounts, and launch your party.";
            _ = CheckForUpdatesAsync();
        }
        catch (InvalidDataException exception)
        {
            AddAccountButton.IsEnabled = false;
            SettingsButton.IsEnabled = false;
            LayoutPicker.IsEnabled = false;
            LaunchVisibleButton.IsEnabled = false;
            GlobalStatusText.Text = "Account data could not be loaded. The original local files were left unchanged.";
            MessageBox.Show(this, exception.Message, "FourFold profile data",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            AddAccountButton.IsEnabled = false;
            SettingsButton.IsEnabled = false;
            LayoutPicker.IsEnabled = false;
            LaunchVisibleButton.IsEnabled = false;
            GlobalStatusText.Text = "The manager could not load local profile data.";
            MessageBox.Show(this, "The local account or panel settings could not be loaded.",
                "FourFold Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var currentVersion = typeof(App).Assembly.GetName().Version ?? new Version(1, 1, 0);
            var currentExecutablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExecutablePath))
            {
                return;
            }

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            var coordinator = new UpdateCoordinator(
                currentVersion,
                currentExecutablePath,
                Environment.ProcessId,
                new GitHubReleaseClient(httpClient, currentVersion),
                new UpdateDownloader(httpClient),
                new UpdateInstaller(),
                PromptForUpdateAsync,
                ShowUpdateFailure,
                () => Application.Current.Shutdown());

            await coordinator.CheckForUpdateAsync();
        }
        catch
        {
            // Startup updates are optional and must never prevent normal app use.
        }
    }

    private Task<bool> PromptForUpdateAsync(UpdateRelease release)
    {
        var notes = string.IsNullOrWhiteSpace(release.Notes)
            ? "A newer version is available."
            : TruncatePlainText(release.Notes, 1200);
        var message = $"FourFold Account Manager {release.Version} is available.\n\n{notes}\n\nDownload and restart now?";
        var result = MessageBox.Show(
            this,
            message,
            "Update available",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    private void ShowUpdateFailure(string message)
    {
        var result = MessageBox.Show(
            this,
            $"{message}\n\nWould you like to open the latest release page manually?",
            "FourFold Account Manager update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/CodySimonds65/FourFoldAccountManager/releases/latest",
                UseShellExecute = true
            });
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static string TruncatePlainText(string value, int maximumLength)
    {
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd() + "…";
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAccountDialog("Add account", "Set a profile label and optional saved login.",
            verifyPlayer: _xpTracker.VerifyPlayerAsync)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var account = AccountProfile.Create(dialog.AccountLabel, _accounts.Count) with
        {
            RankingUsername = dialog.RankingUsername,
            RankingPlayerId = dialog.RankingPlayerId
        };
        var credentials = dialog.Username.Length == 0
            ? null
            : new AccountCredentials(dialog.Username, dialog.Password);
        try
        {
            _credentialStore.Save(account.Id, credentials);
        }
        catch
        {
            MessageBox.Show(this, "The saved login could not be stored in Windows Credential Manager.",
                "FourFold Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _accounts.Add(account);
        try
        {
            await SaveAccountsAsync();
        }
        catch
        {
            _accounts.Remove(account);
            try
            {
                _credentialStore.Delete(account.Id);
            }
            catch
            {
                // Never include credential data in the UI error message.
            }

            UpdateAccountActions();
            MessageBox.Show(this, "The account profile could not be saved.", "FourFold Account Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        AccountsListBox.SelectedItem = account;
        UpdateAccountActions();
        var visibleSlot = -1;
        try
        {
            await UpdateSettingsAsync(currentSettings =>
            {
                visibleSlot = Enumerable.Range(0, PanelLayoutPolicy.GetVisibleSlotCount(currentSettings.Layout))
                    .FirstOrDefault(index => currentSettings.SlotAccountIds[index] is null, -1);
                return visibleSlot >= 0
                    ? PanelLayoutPolicy.Assign(currentSettings, visibleSlot, account.Id)
                    : currentSettings;
            });
        }
        catch
        {
            await RefreshSlotPickersAsync();
            GlobalStatusText.Text = $"Added {account.Label}, but its panel assignment could not be saved. Choose it from a slot menu.";
            return;
        }

        await RefreshSlotPickersAsync();
        GlobalStatusText.Text = visibleSlot >= 0
            ? $"Added {account.Label} and assigned it to slot {visibleSlot + 1}. Press Launch accounts to continue."
            : $"Added {account.Label}. Choose a panel slot to assign it.";
    }

    private async void RenameAccount_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account)
        {
            return;
        }

        AccountCredentials? storedCredentials;
        try
        {
            storedCredentials = _credentialStore.Read(account.Id);
        }
        catch
        {
            MessageBox.Show(this, "The saved login could not be read from Windows Credential Manager.",
                "FourFold Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new AddAccountDialog(
            "Edit profile",
            "Update the label and optional saved login.",
            account.Label,
            storedCredentials?.Username ?? string.Empty,
            storedCredentials?.Password ?? string.Empty,
            account.RankingUsername,
            account.RankingPlayerId,
            _xpTracker.VerifyPlayerAsync)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var replacement = account with
        {
            Label = dialog.AccountLabel,
            RankingUsername = dialog.RankingUsername,
            RankingPlayerId = dialog.RankingPlayerId
        };
        var updatedCredentials = dialog.Username.Length == 0
            ? null
            : new AccountCredentials(dialog.Username, dialog.Password);
        try
        {
            _credentialStore.Save(account.Id, updatedCredentials);
        }
        catch
        {
            MessageBox.Show(this, "The saved login could not be updated in Windows Credential Manager.",
                "FourFold Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ReplaceAccount(replacement);
        AccountsListBox.SelectedItem = replacement;
        try
        {
            await SaveAccountsAsync();
        }
        catch
        {
            ReplaceAccount(account);
            AccountsListBox.SelectedItem = account;
            try
            {
                _credentialStore.Save(account.Id, storedCredentials);
            }
            catch
            {
                // The original local account metadata remains visible if a storage rollback fails.
            }

            MessageBox.Show(this, "The account profile could not be saved.", "FourFold Account Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await RefreshSlotPickersAsync();
        if (_openAccountIds.Contains(account.Id))
            _xpTracker.Start(account.Id, replacement.RankingUsername ??
                (dialog.Username.Length > 0 ? dialog.Username : null), replacement.RankingPlayerId);
        RefreshTrackerRows();
        GlobalStatusText.Text = $"Updated {replacement.Label}.";
    }

    private async void FavoriteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account)
        {
            return;
        }

        var replacement = account with { IsFavorite = !account.IsFavorite };
        ReplaceAccount(replacement);
        AccountsListBox.SelectedItem = replacement;
        UpdateAccountActions();
        try
        {
            await SaveAccountsAsync();
        }
        catch
        {
            ReplaceAccount(account);
            AccountsListBox.SelectedItem = account;
            MessageBox.Show(this, "The account profile could not be saved.", "FourFold Account Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MoveAccountUp_Click(object sender, RoutedEventArgs e) => await MoveSelectedAccountAsync(-1);

    private async void MoveAccountDown_Click(object sender, RoutedEventArgs e) => await MoveSelectedAccountAsync(1);

    private async Task MoveSelectedAccountAsync(int direction)
    {
        if (SelectedAccount is not { } account)
        {
            return;
        }

        var index = _accounts.IndexOf(account);
        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= _accounts.Count)
        {
            return;
        }

        var previous = _accounts.ToArray();
        _accounts.Move(index, targetIndex);
        NormalizeSortOrder();
        AccountsListBox.SelectedItem = _accounts.FirstOrDefault(item => item.Id == account.Id);
        try
        {
            await SaveAccountsAsync();
            await RefreshSlotPickersAsync();
        }
        catch
        {
            RestoreAccountOrder(previous);
            AccountsListBox.SelectedItem = _accounts.FirstOrDefault(item => item.Id == account.Id);
            MessageBox.Show(this, "The account order could not be saved.", "FourFold Account Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Remove '{account.Label}' and clear its saved FourFold browser data? This signs the profile out and cannot be undone.",
            "Remove account profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        AccountCredentials? savedCredentials;
        try
        {
            savedCredentials = _credentialStore.Read(account.Id);
            _credentialStore.Delete(account.Id);
        }
        catch
        {
            MessageBox.Show(this, "The profile's saved login could not be removed from Windows Credential Manager.",
                "Profile removal failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            await CloseAccountViewAsync(account.Id);
            await _browserSessions.ClearProfileAsync(account.Id, MaintenanceWebViewHost);
            await UpdateSettingsAsync(currentSettings =>
                PanelLayoutPolicy.ClearAccount(currentSettings, account.Id));
            _accounts.Remove(account);
            NormalizeSortOrder();
            await SaveAccountsAsync();
            await RebuildPanelAsync(closeExistingViews: false);
            await _xpTracker.RemoveAccountDataAsync(account.Id);
            UpdateAccountActions();
            GlobalStatusText.Text = $"Removed {account.Label} and cleared its browser data.";
        }
        catch
        {
            try
            {
                _credentialStore.Save(account.Id, savedCredentials);
            }
            catch
            {
                // Account and browser errors are reported below without exposing credential data.
            }

            await ReloadLocalDataAfterFailureAsync();
            MessageBox.Show(this,
                "The profile could not be fully cleared or saved. Review the account list and try again.",
                "Profile removal failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AccountsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAccountActions();
        var selectedAccount = AccountsListBox.SelectedItem as AccountProfile;
        CancelProfileRead();
        PluginSidebar.SetSelectedAccount(selectedAccount);
        UpdatePluginSidebarVisibility();
        if (selectedAccount is not null && PluginSidebar.ActivePlugin is PluginKind.ClassComparison or PluginKind.XpCalculator)
        {
            _ = RefreshSelectedProfileAsync();
        }
    }

    private async Task RefreshSelectedProfileAsync()
    {
        var account = SelectedAccount;
        if (account is null)
        {
            PluginSidebar.SetProfileStatus("Select an account to load its profile.");
            return;
        }

        CancelProfileRead();
        var cancellation = new CancellationTokenSource();
        _profileReadCancellation = cancellation;
        var generation = ++_profileReadGeneration;
        PluginSidebar.SetProfileStatus("Loading public profile…");

        string? username = account.RankingUsername;
        if (username is null)
        {
            try { username = _credentialStore.Read(account.Id)?.Username; }
            catch { /* The profile status below explains the missing identity. */ }
        }

        try
        {
            var result = await _playerProfileService.ReadAsync(
                account.Id,
                username,
                account.RankingPlayerId,
                cancellation.Token);
            if (generation != _profileReadGeneration || SelectedAccount?.Id != account.Id)
            {
                return;
            }

            if (result.Snapshot is not null)
            {
                PluginSidebar.SetProfileSnapshot(result.Snapshot);
            }

            if (!result.IsSuccess)
            {
                PluginSidebar.SetProfileStatus(result.Message);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_profileReadCancellation, cancellation))
            {
                _profileReadCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelProfileRead()
    {
        _profileReadGeneration++;
        _profileReadCancellation?.Cancel();
        _profileReadCancellation?.Dispose();
        _profileReadCancellation = null;
    }

    private async void LayoutPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || LayoutPicker.SelectedValue is not PanelLayout layout || layout == _panelSettings.Layout)
        {
            return;
        }

        try
        {
            await UpdateSettingsAsync(currentSettings =>
                PanelLayoutPolicy.WithLayout(currentSettings, layout));
            await RebuildPanelAsync(closeExistingViews: false);
            FullscreenOverlayTray.RevealEdgeTab();
            GlobalStatusText.Text = $"Layout changed to {FormatLayout(layout)}. Slot assignments were preserved.";
        }
        catch
        {
            LayoutPicker.SelectedValue = _panelSettings.Layout;
            MessageBox.Show(this, "The selected layout could not be saved.", "FourFold Account Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleAccountsPanel_Click(object sender, RoutedEventArgs e)
    {
        if (_showingLeaderboard) return;
        _accountsPanelVisible = !_accountsPanelVisible;
        AccountsPanel.Visibility = _accountsPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        AccountsColumn.Width = _accountsPanelVisible ? new GridLength(232) : new GridLength(0);
        AccountsGapColumn.Width = _accountsPanelVisible ? new GridLength(16) : new GridLength(0);

        ToggleAccountsButton.ToolTip = _accountsPanelVisible
            ? "Hide the accounts rail to expand the multi-box panel."
            : "Show account profiles and slot assignments.";
    }

    private void WorkspaceView_Click(object sender, RoutedEventArgs e) => ShowWorkspaceView();

    private void LeaderboardView_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullScreen || _showingLeaderboard) return;
        _showingLeaderboard = true;
        ApplyTopLevelView();
        _leaderboardRefreshTimer.Start();
        LeaderboardPanelView.SetVisible(true);
    }

    private void ShowWorkspaceView()
    {
        if (!_showingLeaderboard) return;
        _showingLeaderboard = false;
        _leaderboardRefreshTimer.Stop();
        LeaderboardPanelView.Stop();
        ApplyTopLevelView();
    }

    private void ApplyTopLevelView()
    {
        PanelGridHost.Visibility = _showingLeaderboard ? Visibility.Collapsed : Visibility.Visible;
        LeaderboardPanelView.Visibility = _showingLeaderboard ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceControls.Visibility = _showingLeaderboard ? Visibility.Collapsed : Visibility.Visible;
        ToggleAccountsButton.Visibility = _showingLeaderboard ? Visibility.Collapsed : Visibility.Visible;
        GlobalStatusText.Visibility = _showingLeaderboard || _isFullScreen
            ? Visibility.Collapsed : Visibility.Visible;
        ViewSubtitle.Text = _showingLeaderboard
            ? "Daily, weekly, and monthly XP gains."
            : "Your accounts, together.";
        var accountsVisible = _showingLeaderboard || _accountsPanelVisible;
        AccountsPanel.Visibility = _isFullScreen || !accountsVisible
            ? Visibility.Collapsed : Visibility.Visible;
        AccountsColumn.Width = _isFullScreen || !accountsVisible
            ? new GridLength(0) : new GridLength(232);
        AccountsGapColumn.Width = _isFullScreen || !accountsVisible
            ? new GridLength(0) : new GridLength(16);
        UpdateTopLevelButtons();
        UpdatePluginSidebarVisibility();
    }

    private void UpdateTopLevelButtons()
    {
        WorkspaceViewButton.Background = (Brush)FindResource(_showingLeaderboard
            ? "Brush.SurfaceRaised" : "Brush.AccentSoft");
        WorkspaceViewButton.Foreground = (Brush)FindResource(_showingLeaderboard
            ? "Brush.TextPrimary" : "Brush.AccentGold");
        LeaderboardViewButton.Background = (Brush)FindResource(_showingLeaderboard
            ? "Brush.AccentSoft" : "Brush.SurfaceRaised");
        LeaderboardViewButton.Foreground = (Brush)FindResource(_showingLeaderboard
            ? "Brush.AccentGold" : "Brush.TextPrimary");
    }

    private void ManageSlots_Click(object sender, RoutedEventArgs e)
    {
        _slotManagementVisible = !_slotManagementVisible;
        UpdateAllSlotPresentations();
        UpdateManageSlotsButton();
    }

    private void AdjustViews_Click(object sender, RoutedEventArgs e)
    {
        _viewAdjustmentVisible = !_viewAdjustmentVisible;
        UpdateAllSlotPresentations();
        UpdateManageSlotsButton();
    }

    private void ToggleLayoutDividerResizing()
    {
        GlobalStatusText.Text = _layoutDividerResizeController.Toggle()
            ? "Layout divider resizing locked."
            : "Layout divider resizing unlocked.";
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(
            _panelSettings.FillGameToPanel,
            _panelSettings.ShowFullScreenExitButton,
            GlobalShortcutActions.All.ToDictionary(
                action => action,
                action => GlobalShortcutActions.GetChord(_panelSettings, action)),
            GlobalShortcutActions.All.Where(action => _shortcuts?.IsAvailable(action) != true).ToHashSet())
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var changedShortcuts = GlobalShortcutActions.All
            .Where(action => dialog.Shortcuts[action] != GlobalShortcutActions.GetChord(_panelSettings, action))
            .ToArray();
        if (dialog.FillGameToPanel == _panelSettings.FillGameToPanel &&
            dialog.ShowFullScreenExitButton == _panelSettings.ShowFullScreenExitButton &&
            changedShortcuts.Length == 0 &&
            !dialog.ResetLayoutSizes)
        {
            return;
        }

        PanelSettings? nextSettings = null;
        var scalingChanged = false;
        SettingsButton.IsEnabled = false;
        try
        {
            PanelSettings WithDialogShortcuts(PanelSettings settings) =>
                GlobalShortcutActions.All.Aggregate(settings,
                    (candidate, action) => GlobalShortcutActions.WithChord(candidate, action, dialog.Shortcuts[action]));

            async Task PersistDialogSettingsAsync()
            {
                nextSettings = await UpdateSettingsAsync(async currentSettings =>
                {
                    var candidate = dialog.ResetLayoutSizes
                        ? PanelLayoutPolicy.ResetSplitStates(currentSettings)
                        : currentSettings;
                    candidate = WithDialogShortcuts(candidate with
                    {
                        FillGameToPanel = dialog.FillGameToPanel,
                        ShowFullScreenExitButton = dialog.ShowFullScreenExitButton
                    });
                    scalingChanged = candidate.FillGameToPanel != currentSettings.FillGameToPanel;
                    if (scalingChanged)
                    {
                        await _browserSessions.SetGameScalingAsync(candidate.FillGameToPanel);
                    }

                    return candidate;
                }, previousSettings => scalingChanged
                    ? _browserSessions.SetGameScalingAsync(previousSettings.FillGameToPanel)
                    : Task.CompletedTask);
            }

            if (changedShortcuts.Length > 0)
            {
                var registered = _shortcuts is not null &&
                    await _shortcuts.ApplyAsync(
                        _panelSettings, WithDialogShortcuts(_panelSettings), PersistDialogSettingsAsync);
                if (!registered)
                {
                    MessageBox.Show(this,
                        "Windows couldn't register one or more new shortcuts. Your previous saved shortcuts and registrations remain unchanged. Choose different combinations and try again.",
                        "Shortcut unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                await PersistDialogSettingsAsync();
            }

            if (nextSettings is null)
            {
                return;
            }

            if (dialog.ResetLayoutSizes)
            {
                await RebuildPanelAsync(closeExistingViews: false);
            }
            if (!nextSettings.FillGameToPanel)
            {
                _viewAdjustmentVisible = false;
                UpdateAllSlotPresentations();
            }
            UpdateManageSlotsButton();
            GlobalStatusText.Text = dialog.ResetLayoutSizes
                ? "Client layout sizes restored to defaults."
                : scalingChanged
                ? nextSettings.FillGameToPanel
                    ? "Game scaling set to Fill panel."
                    : "Game scaling set to Fit entire game."
                : changedShortcuts.Length > 1
                ? "Global shortcuts updated."
                : changedShortcuts.Length == 1
                ? $"{GlobalShortcutActions.DisplayName(changedShortcuts[0])} shortcut updated."
                : nextSettings.ShowFullScreenExitButton
                    ? "Full-screen Exit button enabled."
                    : "Full-screen Exit button hidden. Press Esc to leave full screen.";
        }
        catch
        {
            MessageBox.Show(this, "The settings could not be applied.",
                "FourFold settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SettingsButton.IsEnabled = true;
        }
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        EnterFullScreen();
    }

    private void FullScreenExit_Click(object sender, RoutedEventArgs e)
    {
        ExitFullScreen();
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_isFullScreen && e.Key == System.Windows.Input.Key.Escape)
        {
            ExitFullScreen();
            e.Handled = true;
        }
    }

    private void FullScreenExit_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        FullScreenExitButton.Opacity = 1;

    private void FullScreenExit_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        FullScreenExitButton.Opacity = 0.78;

    private void EnterFullScreen()
    {
        if (_isFullScreen)
        {
            return;
        }

        if (_showingLeaderboard) ShowWorkspaceView();

        _previousWindowState = WindowState;
        _previousWindowStyle = WindowStyle;
        _previousResizeMode = ResizeMode;
        _isFullScreen = true;
        _overlayEditing = false;
        FullscreenOverlayTray.SetFullscreen(true);
        FullscreenOverlayTray.SetEditing(false);

        AppHeaderBorder.Visibility = Visibility.Collapsed;
        AppHeaderRow.Height = new GridLength(0);
        MainContentGrid.Margin = new Thickness(0);
        AccountsPanel.Visibility = Visibility.Collapsed;
        AccountsColumn.Width = new GridLength(0);
        AccountsGapColumn.Width = new GridLength(0);
        UpdatePluginSidebarVisibility();
        PanelToolbar.Visibility = Visibility.Collapsed;
        GlobalStatusText.Visibility = Visibility.Collapsed;
        PanelBorder.Padding = new Thickness(0);
        PanelBorder.CornerRadius = new CornerRadius(0);
        PanelBorder.BorderThickness = new Thickness(0);
        FullScreenExitButton.Visibility = _panelSettings.ShowFullScreenExitButton
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateAllSlotPresentations();
        UpdateManageSlotsButton();

        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        RefreshTrackerRows();
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return;
        }

        WindowState = WindowState.Normal;
        WindowStyle = _previousWindowStyle;
        ResizeMode = _previousResizeMode;

        _overlayEditing = false;
        FullscreenOverlayTray.SetEditing(false);
        FullscreenOverlayTray.SetFullscreen(false);
        _isFullScreen = false;
        AppHeaderBorder.Visibility = Visibility.Visible;
        AppHeaderRow.Height = new GridLength(60);
        MainContentGrid.Margin = new Thickness(16);
        AccountsPanel.Visibility = _accountsPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        AccountsColumn.Width = _accountsPanelVisible ? new GridLength(232) : new GridLength(0);
        AccountsGapColumn.Width = _accountsPanelVisible ? new GridLength(16) : new GridLength(0);
        UpdatePluginSidebarVisibility();
        PanelToolbar.Visibility = Visibility.Visible;
        GlobalStatusText.Visibility = Visibility.Visible;
        PanelBorder.Padding = new Thickness(0);
        PanelBorder.CornerRadius = new CornerRadius(0);
        PanelBorder.BorderThickness = new Thickness(0);
        FullScreenExitButton.Visibility = Visibility.Collapsed;
        UpdateAllSlotPresentations();
        UpdateManageSlotsButton();
        RefreshTrackerRows();

        WindowState = _previousWindowState;
    }

    private void SetOverlayEditing(bool isEditing)
    {
        _overlayEditing = _isFullScreen && isEditing;
        FullscreenOverlayTray.SetEditing(_overlayEditing);
        RefreshTrackerRows();
    }

    private void UpdateManageSlotsButton()
    {
        ManageSlotsButton.Visibility = _openAccountIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ManageSlotsButton.Content = _slotManagementVisible ? "Done" : "Manage slots";
        ManageSlotsButton.ToolTip = _slotManagementVisible
            ? "Hide account selectors for started views."
            : "Show account selectors to change slot assignments.";
        AdjustViewsButton.Visibility = _openAccountIds.Count > 0 && _panelSettings.FillGameToPanel && !_isFullScreen
            ? Visibility.Visible
            : Visibility.Collapsed;
        AdjustViewsButton.Content = _viewAdjustmentVisible ? "Done adjusting" : "Adjust views";
        AdjustViewsButton.ToolTip = _viewAdjustmentVisible
            ? "Hide the per-client sizing controls."
            : "Resize each active game within its account panel.";
    }

    private void RefreshTrackerRows()
    {
        var states = _xpTracker.GetStates().ToDictionary(state => state.AccountId);
        var accountRows = new List<OverlayTrayAccountRow>();
        var editing = _isFullScreen && _overlayEditing;
        _xpTrackerRows.Clear();
        foreach (var slot in _slotCards.OrderBy(slot => slot.SlotIndex))
        {
            var assignedAccountId = _panelSettings.SlotAccountIds[slot.SlotIndex];
            var account = assignedAccountId is { } id
                ? _accounts.FirstOrDefault(profile => profile.Id == id)
                : null;
            var label = account?.Label ?? "Account";
            var isOpen = assignedAccountId is { } openAccountId &&
                _openAccountIds.Contains(openAccountId) && slot.View is not null;
            var cards = new List<OverlayCardModel>();

            if (isOpen && assignedAccountId is { } trackedAccountId)
            {
                XpTrackerRow? trackerRow = null;
                if (states.TryGetValue(trackedAccountId, out var state))
                {
                    trackerRow = XpTrackerRow.FromState(slot.SlotIndex + 1, label, state);
                    _xpTrackerRows.Add(trackerRow);
                }

                if (account is not null)
                {
                    var switches = new List<OverlayTraySwitch>();
                    foreach (var definition in OverlayAddOnCatalog.All.Where(
                                 definition => definition.Scope == OverlayAddOnScope.Account))
                    {
                        AddOverlayAddOn(definition, new OverlayCardKey(definition.Kind, trackedAccountId),
                            label, trackerRow, cards, switches);
                    }

                    accountRows.Add(new OverlayTrayAccountRow(trackedAccountId, label, switches));
                }
            }

            slot.OverlayLayer.SetCards(cards, editing);
        }

        var globalCards = new List<OverlayCardModel>();
        var globalSwitches = new List<OverlayTraySwitch>();
        foreach (var definition in OverlayAddOnCatalog.All.Where(
                     definition => definition.Scope == OverlayAddOnScope.Global))
        {
            AddOverlayAddOn(definition, new OverlayCardKey(definition.Kind, null),
                string.Empty, null, globalCards, globalSwitches);
        }

        GlobalOverlayLayer.SetCards(globalCards, editing);
        FullscreenOverlayTray.SetRows(globalSwitches, accountRows);
        UpdatePluginSidebarVisibility();
    }

    private void AddOverlayAddOn(
        OverlayAddOnDefinition definition,
        OverlayCardKey key,
        string accountLabel,
        XpTrackerRow? trackerRow,
        List<OverlayCardModel> cards,
        List<OverlayTraySwitch> switches)
    {
        if (CreateOverlayCardData(definition.Kind, accountLabel, trackerRow) is not { } data)
        {
            return;
        }

        var placement = OverlayCardPolicy.Get(_panelSettings, key);
        var accessibleName = accountLabel.Length > 0
            ? $"{accountLabel} {definition.DisplayName}"
            : definition.DisplayName;
        switches.Add(new OverlayTraySwitch(key, definition.DisplayName, data.Summary,
            placement?.Enabled == true, accessibleName));
        if (_isFullScreen && placement is { Enabled: true })
        {
            cards.Add(new OverlayCardModel(key, definition, placement.Bounds, cards.Count, data));
        }
    }

    // Each overlay add-on supplies its card data here; a kind without data is not offered in the Overlays panel.
    private static IOverlayCardData? CreateOverlayCardData(OverlayAddOnKind kind, string accountLabel, XpTrackerRow? trackerRow) =>
        kind switch
        {
            OverlayAddOnKind.Xp => new XpOverlayCardData(accountLabel, trackerRow?.XpPerHourText ?? "— XP/hr"),
            _ => null
        };

    private void UpdatePluginSidebarVisibility()
    {
        var visible = PluginSidebar.UpdateHostVisibility(!_showingLeaderboard,
            _isFullScreen, _openAccountIds);
        TrackerGapColumn.Width = visible ? new GridLength(6) : new GridLength(0);
        TrackerColumn.Width = visible ? new GridLength(250) : new GridLength(0);
    }

    private async void FullscreenOverlayTray_CardToggleRequested(object? sender, OverlayCardToggleRequestedEventArgs args)
    {
        if (!_isFullScreen || !_overlayEditing || !OverlayCardPolicy.IsValidKey(args.Key))
        {
            return;
        }

        try
        {
            await UpdateSettingsAsync(settings => OverlayCardPolicy.WithEnabled(settings, args.Key, args.Enabled));
        }
        catch
        {
            MessageBox.Show(this,
                "The overlay card setting could not be saved. Its previous setting was restored.",
                "FourFold Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // Rebuild from saved settings so a failed save also reverts the switch.
            RefreshTrackerRows();
        }
    }

    private async void OverlayLayer_BoundsCommitted(object? sender, OverlayCardBoundsCommittedEventArgs args)
    {
        if (sender is not OverlayCardLayer layer || !_isFullScreen || !_overlayEditing ||
            !IsOverlayCardOwnedByLayer(layer, args.Key))
        {
            return;
        }

        try
        {
            await UpdateSettingsAsync(settings => OverlayCardPolicy.WithBounds(settings, args.Key, args.Bounds));
            RefreshTrackerRows();
        }
        catch
        {
            layer.RestoreSavedBounds();
            MessageBox.Show(this,
                "The overlay placement could not be saved. Its previous position was restored.",
                "FourFold Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool IsOverlayCardOwnedByLayer(OverlayCardLayer layer, OverlayCardKey key)
    {
        if (ReferenceEquals(layer, GlobalOverlayLayer))
        {
            return key.AccountId is null;
        }

        var slot = _slotCards.FirstOrDefault(candidate => ReferenceEquals(candidate.OverlayLayer, layer));
        return slot is not null && key.AccountId is { } accountId &&
            _panelSettings.SlotAccountIds[slot.SlotIndex] == accountId &&
            _openAccountIds.Contains(accountId) && slot.View is not null;
    }

    private void UpdateAllSlotPresentations()
    {
        foreach (var slot in _slotCards)
        {
            UpdateSlotPresentation(slot);
        }
    }

    private void UpdateSlotPresentation(PanelSlotCard slot)
    {
        var assignedAccountId = _panelSettings.SlotAccountIds[slot.SlotIndex];
        var isOpen = assignedAccountId is { } accountId && _openAccountIds.Contains(accountId);
        var hasAssignedAccount = assignedAccountId is not null;
        var account = assignedAccountId is { } id ? _accounts.FirstOrDefault(item => item.Id == id) : null;
        slot.RelaunchButton.Visibility = AssignedAccountLaunchPolicy.ShouldShowRelaunch(
            hasAssignedAccount, isOpen)
            ? Visibility.Visible
            : Visibility.Collapsed;
        slot.RelaunchButton.IsEnabled = _isReady && !_batchLaunchInProgress;
        slot.AccountLabel.Text = account?.Label ?? "Account view";
        slot.EmptyTitle.Text = account is null ? "An open spot in your party" : $"{account.Label} is ready";
        slot.EmptyDescription.Text = _isFullScreen
            ? "Exit full screen to set up this slot."
            : account is null
                ? "Choose an account above to fill this slot."
                : "Select Launch accounts to start playing together.";

        var showPicker = _slotManagementVisible || !isOpen;
        slot.AccountPicker.Visibility = showPicker ? Visibility.Visible : Visibility.Collapsed;
        slot.AccountLabel.Visibility = showPicker ? Visibility.Collapsed : Visibility.Visible;

        slot.Header.Visibility = _isFullScreen ? Visibility.Collapsed : Visibility.Visible;
        slot.Status.Visibility = !_isFullScreen && slot.Tone is StatusTone.Info or StatusTone.Warning or StatusTone.Error
            ? Visibility.Visible : Visibility.Collapsed;
        slot.Root.Margin = _isFullScreen ? new Thickness(0) : new Thickness(4);
        slot.Root.Padding = _isFullScreen ? new Thickness(0) : new Thickness(10);
        slot.Root.BorderThickness = _isFullScreen ? new Thickness(0) : new Thickness(1);
        slot.Root.CornerRadius = _isFullScreen ? new CornerRadius(0) : new CornerRadius(10);
        slot.ViewAdjustmentOverlay.Visibility = _viewAdjustmentVisible && !_isFullScreen &&
            _panelSettings.FillGameToPanel && isOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void LaunchVisible_Click(object sender, RoutedEventArgs e)
    {
        if (_batchLaunchInProgress)
        {
            return;
        }

        var assignedSlots = _slotCards
            .Select(slot => (Slot: slot, AccountId: _panelSettings.SlotAccountIds[slot.SlotIndex]))
            .Where(item => item.AccountId is not null)
            .ToArray();
        if (assignedSlots.Length == 0)
        {
            GlobalStatusText.Text = "Assign at least one profile to a visible slot before starting accounts.";
            return;
        }

        _slotManagementVisible = false;
        UpdateAllSlotPresentations();
        UpdateManageSlotsButton();
        SetBatchLaunchMode(true);
        var outcomes = new List<AssignedAccountStartResult>();
        try
        {
            foreach (var (slot, assignedAccountId) in assignedSlots)
            {
                if (assignedAccountId is not { } accountId)
                {
                    continue;
                }

                if (!AssignedAccountLaunchPolicy.ShouldStart(
                        _openAccountIds.Contains(accountId),
                        _failedAccountIds.Contains(accountId)))
                {
                    outcomes.Add(AssignedAccountStartResult.AlreadyRunning);
                    continue;
                }

                GlobalStatusText.Text = $"Starting account in slot {slot.SlotIndex + 1} of {assignedSlots.Length}…";
                try
                {
                    if (_openAccountIds.Contains(accountId) && _failedAccountIds.Contains(accountId))
                    {
                        await CloseAccountViewAsync(accountId, preserveFailure: true);
                    }

                    var outcome = await StartAssignedAccountAsync(slot, accountId);
                    RecordAccountStartOutcome(accountId, outcome);
                    outcomes.Add(outcome);
                }
                catch (Exception exception)
                {
                    _failedAccountIds.Add(accountId);
                    SetSlotStatus(slot, SafeBrowserError(exception), StatusTone.Error);
                    outcomes.Add(AssignedAccountStartResult.Failed);
                }
            }

            var opened = outcomes.Count(result => result == AssignedAccountStartResult.Opened);
            var alreadyRunning = outcomes.Count(result => result == AssignedAccountStartResult.AlreadyRunning);
            var manual = outcomes.Count(result => result == AssignedAccountStartResult.ManualActionRequired);
            var failed = outcomes.Count(result => result == AssignedAccountStartResult.Failed);
            GlobalStatusText.Text = $"Start complete: {opened} game(s) opened, {alreadyRunning} already running, {manual} need manual action, {failed} failed.";
        }
        finally
        {
            SetBatchLaunchMode(false);
        }
    }

    private async Task<AssignedAccountStartResult> StartAssignedAccountAsync(PanelSlotCard slot, Guid accountId)
    {
        SetSlotStatus(slot, "Opening FourFold sign in…", StatusTone.Info);
        var view = await _browserSessions.CreateViewAsync(accountId, slot.BrowserHost);
        _openAccountIds.Add(accountId);
        var profile = _accounts.FirstOrDefault(account => account.Id == accountId);
        string? rankingUsername = profile?.RankingUsername;
        if (rankingUsername is null)
        {
            try { rankingUsername = _credentialStore.Read(accountId)?.Username; }
            catch { /* The row will ask for a ranking username. */ }
        }
        _xpTracker.Start(accountId, rankingUsername, profile?.RankingPlayerId);
        RefreshTrackerRows();
        AttachBrowserView(slot, accountId, view);
        _ = SyncLeaderboardParticipationAsync();

        var loginPageResult = await _browserSessions.NavigateAndWaitAsync(accountId, FourFoldDestination.LoginUri);
        if (loginPageResult != BrowserNavigationResult.Navigated)
        {
            SetSlotStatus(slot, NavigationFailureMessage(loginPageResult, "sign-in page"), StatusTone.Error);
            return AssignedAccountStartResult.Failed;
        }

        AccountCredentials? credentials;
        try
        {
            credentials = _credentialStore.Read(accountId);
        }
        catch
        {
            SetSlotStatus(slot, "Saved login could not be read. Sign in manually in this slot.", StatusTone.Warning);
            return AssignedAccountStartResult.ManualActionRequired;
        }

        if (credentials is null)
        {
            SetSlotStatus(slot, "Sign-in page ready. Add a saved login or sign in manually in this slot.", StatusTone.Warning);
            return AssignedAccountStartResult.ManualActionRequired;
        }

        SetSlotStatus(slot, "Submitting this profile's saved login…", StatusTone.Info);
        var submissionResult = await _browserSessions.SubmitSavedLoginAsync(accountId, credentials);
        if (submissionResult != LoginSubmissionResult.Submitted)
        {
            SetSlotStatus(slot, LoginSubmissionFailureMessage(submissionResult), StatusTone.Error);
            return AssignedAccountStartResult.Failed;
        }

        SetSlotStatus(slot, "Login form submitted. Opening FourFold in this browser profile…", StatusTone.Info);
        var gamePageResult = await _browserSessions.NavigateAndWaitAsync(accountId, FourFoldDestination.StartUri);
        if (gamePageResult != BrowserNavigationResult.Navigated)
        {
            SetSlotStatus(slot, NavigationFailureMessage(gamePageResult, "browser game page"), StatusTone.Error);
            return AssignedAccountStartResult.Failed;
        }

        SetSlotStatus(slot, "Selecting Play now for the browser game…", StatusTone.Info);
        var playResult = await _browserSessions.SelectPlayInBrowserAsync(accountId);
        if (playResult == PlayInBrowserResult.Activated)
        {
            SetSlotStatus(slot, "Play now selected. FourFold is loading in this account panel.", StatusTone.Success);
            return AssignedAccountStartResult.Opened;
        }

        if (playResult is PlayInBrowserResult.ControlNotFound or PlayInBrowserResult.NotOnPlayPage)
        {
            SetSlotStatus(slot, "Select Play now under Play in browser to finish opening the game.", StatusTone.Warning);
            return AssignedAccountStartResult.ManualActionRequired;
        }

        SetSlotStatus(slot, "The account view closed before Play now could be selected.", StatusTone.Error);
        return AssignedAccountStartResult.Failed;
    }

    private void RecordAccountStartOutcome(Guid accountId, AssignedAccountStartResult outcome)
    {
        if (outcome == AssignedAccountStartResult.Failed)
        {
            _failedAccountIds.Add(accountId);
            return;
        }

        _failedAccountIds.Remove(accountId);
    }

    private void SetBatchLaunchMode(bool isActive)
    {
        _batchLaunchInProgress = isActive;
        LaunchVisibleButton.IsEnabled = !isActive && _isReady;
        LaunchVisibleButton.Content = isActive ? "Launching…" : "Launch accounts";
        LayoutPicker.IsEnabled = !isActive && _isReady;
        AddAccountButton.IsEnabled = !isActive && _isReady;
        SettingsButton.IsEnabled = !isActive && _isReady;
        AdjustViewsButton.IsEnabled = !isActive && _isReady;
        AccountsListBox.IsEnabled = !isActive;
        RenameAccountButton.IsEnabled = !isActive && SelectedAccount is not null;
        FavoriteAccountButton.IsEnabled = !isActive && SelectedAccount is not null;
        MoveUpButton.IsEnabled = !isActive && SelectedAccount is not null && AccountsListBox.SelectedIndex > 0;
        MoveDownButton.IsEnabled = !isActive && SelectedAccount is not null && AccountsListBox.SelectedIndex < _accounts.Count - 1;
        RemoveAccountButton.IsEnabled = !isActive && SelectedAccount is not null;

        foreach (var slot in _slotCards)
        {
            slot.AccountPicker.IsEnabled = !isActive;
            slot.RelaunchButton.IsEnabled = !isActive && _isReady;
        }
    }

    private async void RelaunchAccount_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady || _batchLaunchInProgress || sender is not Button { Tag: int slotIndex })
        {
            return;
        }

        var slot = _slotCards.FirstOrDefault(item => item.SlotIndex == slotIndex);
        if (slot is null || slotIndex < 0 || slotIndex >= _panelSettings.SlotAccountIds.Count ||
            _panelSettings.SlotAccountIds[slotIndex] is not { } accountId)
        {
            return;
        }

        SetBatchLaunchMode(true);
        try
        {
            await CloseAccountViewAsync(accountId, preserveFailure: true);
            var outcome = await StartAssignedAccountAsync(slot, accountId);
            RecordAccountStartOutcome(accountId, outcome);
            GlobalStatusText.Text = outcome switch
            {
                AssignedAccountStartResult.Opened => $"Relaunch complete: account in slot {slotIndex + 1} opened.",
                AssignedAccountStartResult.ManualActionRequired => $"Relaunch ready: finish sign-in in slot {slotIndex + 1}.",
                _ => $"Relaunch failed for account in slot {slotIndex + 1}. Check the slot status for details."
            };
        }
        catch (Exception exception)
        {
            _failedAccountIds.Add(accountId);
            SetSlotStatus(slot, SafeBrowserError(exception), StatusTone.Error);
            GlobalStatusText.Text = $"Relaunch failed for account in slot {slotIndex + 1}. Check the slot status for details.";
        }
        finally
        {
            SetBatchLaunchMode(false);
        }
    }

    private static string NavigationFailureMessage(BrowserNavigationResult result, string pageName) => result switch
    {
        BrowserNavigationResult.TimedOut => $"Timed out while loading the {pageName}. Press Launch accounts to retry.",
        BrowserNavigationResult.Blocked => $"FourFold navigation to the {pageName} was blocked.",
        BrowserNavigationResult.Failed => $"The {pageName} could not be loaded. Press Launch accounts to retry.",
        BrowserNavigationResult.ViewNotOpen => "The browser view is not open. Press Launch accounts to retry.",
        _ => $"The {pageName} could not be loaded. Press Launch accounts to retry."
    };

    private static string LoginSubmissionFailureMessage(LoginSubmissionResult result) => result switch
    {
        LoginSubmissionResult.ViewNotOpen => "The browser view closed before sign-in. Press Launch accounts to retry.",
        LoginSubmissionResult.NotOnLoginPage => "FourFold sign-in page changed. Press Launch accounts to retry.",
        LoginSubmissionResult.LoginFieldsNotFound => "FourFold's login form was not recognized. Nothing was submitted.",
        LoginSubmissionResult.NavigationFailed => "FourFold could not complete the login request. Sign in manually.",
        LoginSubmissionResult.TimedOut => "FourFold sign-in timed out. The form may have been submitted; check this slot.",
        _ => "FourFold sign-in could not be submitted. Sign in manually."
    };

    private async void SlotAccountPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || sender is not ComboBox { Tag: int slotIndex, SelectedItem: SlotAccountChoice choice })
        {
            return;
        }

        if (_panelSettings.SlotAccountIds[slotIndex] == choice.AccountId)
        {
            return;
        }

        try
        {
            await UpdateSettingsAsync(currentSettings =>
                PanelLayoutPolicy.Assign(currentSettings, slotIndex, choice.AccountId));
            await RebuildPanelAsync(closeExistingViews: false);
            GlobalStatusText.Text = choice.AccountId is null
                ? "Slot cleared. Other saved assignments were preserved."
                : $"{choice.Label} assigned. Repeated account assignments move to the new slot.";
        }
        catch
        {
            if (sender is ComboBox picker)
            {
                picker.SelectedItem = picker.Items.Cast<SlotAccountChoice>()
                    .FirstOrDefault(item => item.AccountId == _panelSettings.SlotAccountIds[slotIndex]);
            }

            MessageBox.Show(this, "The slot assignment could not be saved.", "FourFold Account Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RebuildPanelAsync(bool closeExistingViews)
    {
        if (closeExistingViews)
        {
            await CloseAllOpenViewsAsync();
        }
        else
        {
            foreach (var slot in _slotCards)
            {
                DetachSlotEventHandlers(slot);
            }
        }

        _layoutDividerResizeController.Reset();
        _slotCards.Clear();
        PanelGridHost.Children.Clear();
        PanelGridHost.RowDefinitions.Clear();
        PanelGridHost.ColumnDefinitions.Clear();

        PanelGridHost.Children.Add(BuildLayoutNode(
            PanelLayoutPolicy.GetLayoutTree(_panelSettings.Layout)));

        if (!closeExistingViews)
        {
            await RestoreVisibleOpenViewsAsync();
        }

        UpdateManageSlotsButton();
        UpdateAllSlotPresentations();
        _xpTracker.RefreshActiveAccounts(_slotCards
            .Select(slot => _panelSettings.SlotAccountIds[slot.SlotIndex])
            .OfType<Guid>().Where(_openAccountIds.Contains).ToArray());
        RefreshTrackerRows();
    }

    private FrameworkElement BuildLayoutNode(PanelLayoutNode node) =>
        BuildLayoutNode(
            node,
            _panelSettings,
            BuildSlotElement,
            async state =>
            {
                await UpdateSettingsAsync(currentSettings =>
                    PanelLayoutPolicy.WithSplitState(currentSettings, state));
                GlobalStatusText.Text = "The layout sizes were saved.";
            },
            () => MessageBox.Show(this, "The row heights could not be saved.", "FourFold Account Manager",
                MessageBoxButton.OK, MessageBoxImage.Error),
            _layoutDividerResizeController);

    internal static FrameworkElement BuildLayoutNode(
        PanelLayoutNode node,
        PanelSettings settings,
        Func<int, FrameworkElement> buildSlot,
        Func<PanelSplitState, Task> persistSplitState,
        Action reportSaveFailure,
        LayoutDividerResizeController? dividerResizeController = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(buildSlot);
        ArgumentNullException.ThrowIfNull(persistSplitState);
        ArgumentNullException.ThrowIfNull(reportSaveFailure);

        return node switch
        {
            PanelSlotNode slot => buildSlot(slot.SlotIndex),
            PanelSplitNode split => BuildSplitElement(
                split, settings, buildSlot, persistSplitState, reportSaveFailure, dividerResizeController),
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };
    }

    private FrameworkElement BuildSlotElement(int slotIndex)
    {
        var card = CreatePanelSlot(slotIndex);
        _slotCards.Add(card);
        return card.Root;
    }

    private static Grid BuildSplitElement(
        PanelSplitNode split,
        PanelSettings settings,
        Func<int, FrameworkElement> buildSlot,
        Func<PanelSplitState, Task> persistSplitState,
        Action reportSaveFailure,
        LayoutDividerResizeController? dividerResizeController)
    {
        const double minimumWeight = 0.30;
        var group = new Grid();
        var weights = PanelSplitMath.ClampToMinimum(
            PanelLayoutPolicy.GetSplitState(settings, split.Id).Weights,
            minimumWeight).ToArray();

        for (var index = 0; index < split.Children.Count; index++)
        {
            if (split.Orientation == PanelSplitOrientation.Horizontal)
            {
                group.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(weights[index], GridUnitType.Star)
                });
            }
            else
            {
                group.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(weights[index], GridUnitType.Star)
                });
            }

            var child = BuildLayoutNode(
                split.Children[index], settings, buildSlot, persistSplitState, reportSaveFailure,
                dividerResizeController);
            if (split.Orientation == PanelSplitOrientation.Horizontal)
            {
                Grid.SetColumn(child, index);
            }
            else
            {
                Grid.SetRow(child, index);
            }

            group.Children.Add(child);
        }

        var splitters = new List<GridSplitter>();
        for (var boundaryIndex = 0; boundaryIndex < split.Children.Count - 1; boundaryIndex++)
        {
            var splitter = CreateGridSplitter(split.Orientation, boundaryIndex);
            dividerResizeController?.Track(splitter);
            splitters.Add(splitter);
            group.Children.Add(splitter);
        }

        ApplyTrackMinimums(group, split.Orientation, minimumWeight);
        group.SizeChanged += (_, _) => ApplyTrackMinimums(group, split.Orientation, minimumWeight);

        foreach (var (splitter, boundaryIndex) in splitters.Select((splitter, index) => (splitter, index)))
        {
            IReadOnlyList<double>? dragStartWeights = null;

            splitter.DragStarted += (_, _) =>
            {
                dragStartWeights = ReadDefinitionWeights(group, split.Orientation, minimumWeight);
                weights = dragStartWeights.ToArray();
            };
            splitter.DragDelta += (_, eventArgs) =>
            {
                if (dragStartWeights is null)
                {
                    return;
                }

                var groupLength = split.Orientation == PanelSplitOrientation.Horizontal
                    ? group.ActualWidth
                    : group.ActualHeight;
                if (!IsUsableGroupLength(groupLength))
                {
                    return;
                }

                var deltaPixels = split.Orientation == PanelSplitOrientation.Horizontal
                    ? eventArgs.HorizontalChange
                    : eventArgs.VerticalChange;
                var pairWeight = weights[boundaryIndex] + weights[boundaryIndex + 1];
                if (!double.IsFinite(deltaPixels) || pairWeight <= 0)
                {
                    return;
                }

                var pairMinimum = Math.Min(0.5, minimumWeight / pairWeight);
                var adjustedPair = PanelSplitMath.AdjustBoundary(
                    new[] { weights[boundaryIndex], weights[boundaryIndex + 1] },
                    0,
                    deltaPixels / groupLength / pairWeight,
                    pairMinimum);
                var adjustedWeights = weights.ToArray();
                adjustedWeights[boundaryIndex] = adjustedPair[0] * pairWeight;
                adjustedWeights[boundaryIndex + 1] = adjustedPair[1] * pairWeight;
                weights = adjustedWeights;
                ApplyTrackWeights(group, split.Orientation, weights);
                group.UpdateLayout();
            };
            splitter.DragCompleted += async (_, eventArgs) =>
            {
                if (dragStartWeights is null)
                {
                    return;
                }

                var previousWeights = dragStartWeights;
                dragStartWeights = null;
                if (eventArgs.Canceled)
                {
                    weights = previousWeights.ToArray();
                    ApplyTrackWeights(group, split.Orientation, weights);
                    return;
                }

                group.UpdateLayout();
                var completedWeights = ReadActualWeights(group, split.Orientation, minimumWeight);
                if (completedWeights is null)
                {
                    weights = previousWeights.ToArray();
                    ApplyTrackWeights(group, split.Orientation, weights);
                    return;
                }

                weights = completedWeights.ToArray();
                ApplyTrackWeights(group, split.Orientation, weights);
                if (WeightsEqual(previousWeights, weights))
                {
                    return;
                }

                foreach (var groupSplitter in splitters)
                {
                    if (dividerResizeController is not null)
                    {
                        dividerResizeController.SetTemporarilyDisabled(groupSplitter, true);
                    }
                    else
                    {
                        groupSplitter.IsEnabled = false;
                    }
                }

                try
                {
                    await persistSplitState(new PanelSplitState(
                        split.Id,
                        Array.AsReadOnly(weights.ToArray())));
                }
                catch
                {
                    weights = previousWeights.ToArray();
                    ApplyTrackWeights(group, split.Orientation, weights);
                    group.UpdateLayout();
                    reportSaveFailure();
                }
                finally
                {
                    foreach (var groupSplitter in splitters)
                    {
                        if (dividerResizeController is not null)
                        {
                            dividerResizeController.SetTemporarilyDisabled(groupSplitter, false);
                        }
                        else
                        {
                            groupSplitter.IsEnabled = true;
                        }
                    }
                }
            };
        }

        return group;
    }

    private static GridSplitter CreateGridSplitter(PanelSplitOrientation orientation, int boundaryIndex)
    {
        var splitter = new GridSplitter
        {
            ResizeBehavior = GridResizeBehavior.CurrentAndNext,
            ShowsPreview = false,
            Background = Brushes.Transparent,
            Focusable = false
        };

        if (orientation == PanelSplitOrientation.Horizontal)
        {
            splitter.Width = 10;
            splitter.HorizontalAlignment = HorizontalAlignment.Right;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
            splitter.ResizeDirection = GridResizeDirection.Columns;
            splitter.Cursor = System.Windows.Input.Cursors.SizeWE;
            splitter.Margin = new Thickness(0, 4, -5, 4);
            Grid.SetColumn(splitter, boundaryIndex);
        }
        else
        {
            splitter.Height = 10;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Bottom;
            splitter.ResizeDirection = GridResizeDirection.Rows;
            splitter.Cursor = System.Windows.Input.Cursors.SizeNS;
            splitter.Margin = new Thickness(4, 0, 4, -5);
            Grid.SetRow(splitter, boundaryIndex);
        }

        Panel.SetZIndex(splitter, 100);
        return splitter;
    }

    private static void ApplyTrackMinimums(
        Grid group,
        PanelSplitOrientation orientation,
        double minimumWeight)
    {
        var groupLength = orientation == PanelSplitOrientation.Horizontal
            ? group.ActualWidth
            : group.ActualHeight;
        var minimumLength = double.IsFinite(groupLength) && groupLength > 0
            ? groupLength * minimumWeight
            : 0;

        if (orientation == PanelSplitOrientation.Horizontal)
        {
            foreach (var column in group.ColumnDefinitions)
            {
                column.MinWidth = minimumLength;
            }
        }
        else
        {
            foreach (var row in group.RowDefinitions)
            {
                row.MinHeight = minimumLength;
            }
        }
    }

    private static IReadOnlyList<double> ReadDefinitionWeights(
        Grid group,
        PanelSplitOrientation orientation,
        double minimumWeight)
    {
        var values = orientation == PanelSplitOrientation.Horizontal
            ? group.ColumnDefinitions.Select(column => column.Width.Value).ToArray()
            : group.RowDefinitions.Select(row => row.Height.Value).ToArray();
        return PanelSplitMath.ClampToMinimum(values, minimumWeight);
    }

    private static IReadOnlyList<double>? ReadActualWeights(
        Grid group,
        PanelSplitOrientation orientation,
        double minimumWeight)
    {
        var values = orientation == PanelSplitOrientation.Horizontal
            ? group.ColumnDefinitions.Select(column => column.ActualWidth).ToArray()
            : group.RowDefinitions.Select(row => row.ActualHeight).ToArray();
        return IsUsableGroupLength(values.Sum()) &&
               values.All(value => double.IsFinite(value) && value > 0)
            ? PanelSplitMath.ClampToMinimum(values, minimumWeight)
            : null;
    }

    private static bool IsUsableGroupLength(double value) =>
        double.IsFinite(value) && value >= 1;

    private static void ApplyTrackWeights(
        Grid group,
        PanelSplitOrientation orientation,
        IReadOnlyList<double> weights)
    {
        if (orientation == PanelSplitOrientation.Horizontal)
        {
            for (var index = 0; index < group.ColumnDefinitions.Count; index++)
            {
                group.ColumnDefinitions[index].Width = new GridLength(weights[index], GridUnitType.Star);
            }
        }
        else
        {
            for (var index = 0; index < group.RowDefinitions.Count; index++)
            {
                group.RowDefinitions[index].Height = new GridLength(weights[index], GridUnitType.Star);
            }
        }
    }

    private static bool WeightsEqual(IReadOnlyList<double> left, IReadOnlyList<double> right) =>
        left.Count == right.Count &&
        left.Zip(right).All(pair => Math.Abs(pair.First - pair.Second) < 1e-9);

    private async Task RestoreVisibleOpenViewsAsync()
    {
        var visibleAccountIds = _slotCards
            .Select(slot => _panelSettings.SlotAccountIds[slot.SlotIndex])
            .OfType<Guid>()
            .ToHashSet();

        foreach (var accountId in _openAccountIds.ToArray())
        {
            if (!visibleAccountIds.Contains(accountId))
            {
                await CloseAccountViewAsync(accountId);
            }
        }

        foreach (var slot in _slotCards)
        {
            if (_panelSettings.SlotAccountIds[slot.SlotIndex] is not { } accountId ||
                !_openAccountIds.Contains(accountId))
            {
                continue;
            }

            try
            {
                var view = await _browserSessions.CreateViewAsync(accountId, slot.BrowserHost);
                AttachBrowserView(slot, accountId, view);
                SetSlotStatus(slot, "View kept open after the panel change.", StatusTone.Neutral);
            }
            catch (Exception exception)
            {
                SetSlotStatus(slot, SafeBrowserError(exception), StatusTone.Error);
                await CloseAccountViewAsync(accountId);
            }
        }
    }

    private PanelSlotCard CreatePanelSlot(int slotIndex)
    {
        var root = new Border
        {
            Style = (Style)FindResource("PanelCardStyle"),
            Margin = new Thickness(4),
            Padding = new Thickness(10)
        };
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Child = content;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8),
            Background = (Brush)FindResource("Brush.AccentSoft"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"{slotIndex + 1:00}", FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Brush.AccentGold"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        header.Children.Add(badge);
        var relaunchButton = new Button
        {
            Tag = slotIndex,
            Width = 28,
            Height = 28,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true,
            IsTabStop = true,
            ToolTip = "Relaunch this account",
            Content = new TextBlock
            {
                Text = "⟳",
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol"),
                FontSize = 19,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(
            relaunchButton, "Relaunch account in this slot");
        var relaunchStyle = new Style(typeof(Button), (Style)FindResource("AppButtonStyle"));
        relaunchStyle.Setters.Add(new Setter(UIElement.OpacityProperty, 0d));
        relaunchStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(UIElement.IsMouseOver))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Grid), 1)
            },
            Value = true,
            Setters = { new Setter(UIElement.OpacityProperty, 1d) }
        });
        relaunchStyle.Triggers.Add(new Trigger
        {
            Property = UIElement.IsKeyboardFocusedProperty,
            Value = true,
            Setters = { new Setter(UIElement.OpacityProperty, 1d) }
        });
        relaunchButton.Style = relaunchStyle;
        Grid.SetColumn(relaunchButton, 0);
        header.Children.Add(relaunchButton);
        relaunchButton.Click += RelaunchAccount_Click;
        var accountPicker = new ComboBox
        {
            Tag = slotIndex, Height = 34, MinWidth = 100,
            Style = (Style)FindResource("ComboBoxStyle"),
            ItemTemplate = (DataTemplate)FindResource("ChoiceLabelTemplate"),
            SelectedValuePath = nameof(SlotAccountChoice.AccountId),
            ItemsSource = CreateSlotChoices(),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(accountPicker, $"Account for slot {slotIndex + 1}");
        var assignedId = _panelSettings.SlotAccountIds[slotIndex];
        accountPicker.SelectedItem = accountPicker.Items.Cast<SlotAccountChoice>()
            .FirstOrDefault(choice => choice.AccountId == assignedId);
        accountPicker.SelectionChanged += SlotAccountPicker_SelectionChanged;
        Grid.SetColumn(accountPicker, 1);
        header.Children.Add(accountPicker);

        var accountLabel = new TextBlock
        {
            Foreground = (Brush)FindResource("Brush.TextPrimary"), FontWeight = FontWeights.SemiBold,
            FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(accountLabel, 1);
        header.Children.Add(accountLabel);
        content.Children.Add(header);

        var status = new TextBlock
        {
            Style = (Style)FindResource("QuietTextStyle"), Margin = new Thickness(2, 0, 2, 8),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(status, 1);
        content.Children.Add(status);
        var browserHost = new Grid { Background = (Brush)FindResource("Brush.ClientBackground") };
        var placeholder = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20), MaxWidth = 320, IsHitTestVisible = false
        };
        var icon = new Grid { Width = 44, Height = 44, Margin = new Thickness(0, 0, 0, 18) };
        for (var index = 0; index < 4; index++)
        {
            icon.Children.Add(new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(5),
                BorderBrush = (Brush)FindResource(index == slotIndex ? "Brush.AccentGold" : "Brush.BorderStrong"),
                BorderThickness = new Thickness(1),
                Background = index == slotIndex ? (Brush)FindResource("Brush.AccentSoft") : Brushes.Transparent,
                HorizontalAlignment = index % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = index < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom
            });
        }
        placeholder.Children.Add(icon);
        var emptyTitle = new TextBlock
        {
            FontSize = 16, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            Foreground = (Brush)FindResource("Brush.TextSecondary"), TextWrapping = TextWrapping.Wrap
        };
        var emptyDescription = new TextBlock
        {
            FontSize = 12, Margin = new Thickness(0, 7, 0, 0), TextAlignment = TextAlignment.Center,
            Foreground = (Brush)FindResource("Brush.TextMuted"), TextWrapping = TextWrapping.Wrap
        };
        placeholder.Children.Add(emptyTitle);
        placeholder.Children.Add(emptyDescription);
        browserHost.Children.Add(placeholder);
        var overlayLayer = new OverlayCardLayer();
        Panel.SetZIndex(overlayLayer, 50);
        browserHost.Children.Add(overlayLayer);

        var viewportSize = assignedId is { } accountId
            ? PanelLayoutPolicy.GetGameViewportSize(_panelSettings, accountId)
            : GameViewportSize.Default;
        var adjustmentContent = new StackPanel();
        var adjustmentHeader = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        adjustmentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        adjustmentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        adjustmentHeader.Children.Add(new TextBlock
        {
            Text = "Game size",
            Foreground = (Brush)FindResource("Brush.TextPrimary"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var resetViewButton = new Button
        {
            Content = "Reset",
            Height = 28,
            Padding = new Thickness(9, 0, 9, 0),
            Style = (Style)FindResource("AppButtonStyle")
        };
        Grid.SetColumn(resetViewButton, 1);
        adjustmentHeader.Children.Add(resetViewButton);
        adjustmentContent.Children.Add(adjustmentHeader);
        adjustmentContent.Children.Add(CreateViewportAdjustmentRow(
            "Width", "GameViewportWidth", viewportSize.WidthPercent, out var widthSlider, out var widthValue));
        adjustmentContent.Children.Add(CreateViewportAdjustmentRow(
            "Height", "GameViewportHeight", viewportSize.HeightPercent, out var heightSlider, out var heightValue));
        var adjustmentOverlay = new Border
        {
            Tag = "GameViewportAdjustment",
            MaxWidth = 310,
            Padding = new Thickness(12),
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("Brush.BorderStrong"),
            Background = (Brush)FindResource("Brush.Surface"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            Child = adjustmentContent
        };
        Panel.SetZIndex(adjustmentOverlay, 100);
        browserHost.Children.Add(adjustmentOverlay);
        Grid.SetRow(browserHost, 2);
        content.Children.Add(browserHost);
        var slot = new PanelSlotCard(slotIndex, root, header, relaunchButton, accountPicker, accountLabel, status, browserHost,
            overlayLayer, placeholder, emptyTitle, emptyDescription, adjustmentOverlay, widthSlider, heightSlider,
            widthValue, heightValue);
        overlayLayer.BoundsCommitted += OverlayLayer_BoundsCommitted;
        widthSlider.ValueChanged += (_, _) => ScheduleViewportSizeUpdate(slot);
        heightSlider.ValueChanged += (_, _) => ScheduleViewportSizeUpdate(slot);
        resetViewButton.Click += (_, _) => SetViewportSliderValues(slot, GameViewportSize.Default);
        slot.ViewportUpdateTimer.Tick += async (_, _) =>
        {
            slot.ViewportUpdateTimer.Stop();
            await ApplyViewportSizeAsync(slot);
        };
        UpdateSlotPresentation(slot);
        return slot;
    }

    private Grid CreateViewportAdjustmentRow(
        string label,
        string tag,
        double initialValue,
        out Slider slider,
        out TextBlock valueText)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("Brush.TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center
        });
        slider = new Slider
        {
            Tag = tag,
            Minimum = GameViewportSize.MinimumPercent,
            Maximum = GameViewportSize.MaximumPercent,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            IsMoveToPointEnabled = true,
            Value = initialValue,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, $"Game {label.ToLowerInvariant()} percentage");
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        valueText = new TextBlock
        {
            Text = $"{initialValue:0}%",
            Foreground = (Brush)FindResource("Brush.TextPrimary"),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueText, 2);
        row.Children.Add(valueText);
        return row;
    }

    private void ScheduleViewportSizeUpdate(PanelSlotCard slot)
    {
        slot.WidthValue.Text = $"{slot.WidthSlider.Value:0}%";
        slot.HeightValue.Text = $"{slot.HeightSlider.Value:0}%";
        if (!_isReady || slot.SuppressViewportEvents)
        {
            return;
        }

        slot.ViewportUpdateTimer.Stop();
        slot.ViewportUpdateTimer.Start();
    }

    private async Task ApplyViewportSizeAsync(PanelSlotCard slot)
    {
        if (_panelSettings.SlotAccountIds[slot.SlotIndex] is not { } accountId)
        {
            return;
        }

        var nextSize = new GameViewportSize(slot.WidthSlider.Value, slot.HeightSlider.Value);
        await _viewportSaveGate.WaitAsync();
        try
        {
            GameViewportSize? previousSize = null;
            try
            {
                await UpdateSettingsAsync(async currentSettings =>
                {
                    previousSize = PanelLayoutPolicy.GetGameViewportSize(currentSettings, accountId);
                    if (nextSize == previousSize)
                    {
                        return currentSettings;
                    }

                    await _browserSessions.SetGameViewportSizeAsync(accountId, nextSize);
                    return PanelLayoutPolicy.WithGameViewportSize(currentSettings, accountId, nextSize);
                }, currentSettings => _browserSessions.SetGameViewportSizeAsync(
                    accountId,
                    PanelLayoutPolicy.GetGameViewportSize(currentSettings, accountId)));
                GlobalStatusText.Text = $"{slot.AccountLabel.Text} game size set to {nextSize.WidthPercent:0}% × {nextSize.HeightPercent:0}%.";
            }
            catch
            {
                if (previousSize is not null)
                {
                    SetViewportSliderValues(slot, previousSize);
                }
                SetSlotStatus(slot, "The game size could not be saved.", StatusTone.Error);
            }
        }
        finally
        {
            _viewportSaveGate.Release();
        }
    }

    private void SetViewportSliderValues(PanelSlotCard slot, GameViewportSize size)
    {
        slot.SuppressViewportEvents = true;
        try
        {
            slot.WidthSlider.Value = size.WidthPercent;
            slot.HeightSlider.Value = size.HeightPercent;
            slot.WidthValue.Text = $"{size.WidthPercent:0}%";
            slot.HeightValue.Text = $"{size.HeightPercent:0}%";
        }
        finally
        {
            slot.SuppressViewportEvents = false;
        }

        if (_isReady)
        {
            slot.ViewportUpdateTimer.Stop();
            slot.ViewportUpdateTimer.Start();
        }
    }

    private IReadOnlyList<SlotAccountChoice> CreateSlotChoices()
    {
        var choices = new List<SlotAccountChoice> { new(null, "Choose account") };
        choices.AddRange(_accounts.Select(account => new SlotAccountChoice(account.Id, account.Label)));
        return choices;
    }

    private async Task RefreshSlotPickersAsync()
    {
        if (_slotCards.Count == 0)
        {
            await RebuildPanelAsync(closeExistingViews: false);
            return;
        }

        foreach (var slot in _slotCards)
        {
            var choices = CreateSlotChoices();
            slot.AccountPicker.SelectionChanged -= SlotAccountPicker_SelectionChanged;
            slot.AccountPicker.ItemsSource = choices;
            slot.AccountPicker.SelectedItem = choices.FirstOrDefault(
                choice => choice.AccountId == _panelSettings.SlotAccountIds[slot.SlotIndex]);
            slot.AccountPicker.SelectionChanged += SlotAccountPicker_SelectionChanged;
            UpdateSlotPresentation(slot);
        }
    }

    private void AttachBrowserView(PanelSlotCard slot, Guid accountId, WebView2CompositionControl view)
    {
        if (!ReferenceEquals(view.Parent, slot.BrowserHost))
        {
            DetachBrowserView(view);
            slot.BrowserHost.Children.Add(view);
        }

        Panel.SetZIndex(view, 0);

        slot.View = view;
        slot.Placeholder.Visibility = Visibility.Collapsed;
        UpdateSlotPresentation(slot);
        UpdateManageSlotsButton();
        if (slot.NavigationCompletedHandler is not null)
        {
            view.CoreWebView2.NavigationCompleted -= slot.NavigationCompletedHandler;
        }

        slot.NavigationCompletedHandler = (_, args) =>
        {
            var batchLaunchWasActive = _batchLaunchInProgress;
            Dispatcher.BeginInvoke(() =>
            {
                if (!AssignedAccountLaunchPolicy.IsCurrentView(slot.View, view))
                {
                    return;
                }

                if (batchLaunchWasActive || _batchLaunchInProgress)
                {
                    return;
                }

                if (args.IsSuccess)
                {
                    SetSlotStatus(slot, "Page loaded. FourFold sign-in state is not read by the manager.", StatusTone.Neutral);
                }
                else
                {
                    _failedAccountIds.Add(accountId);
                    SetSlotStatus(slot, "Page could not be loaded. Press Launch accounts to retry.", StatusTone.Error);
                }
            }, DispatcherPriority.Background);
        };
        view.CoreWebView2.NavigationCompleted += slot.NavigationCompletedHandler;

        if (slot.ProcessFailedHandler is not null)
        {
            view.CoreWebView2.ProcessFailed -= slot.ProcessFailedHandler;
        }

        slot.ProcessFailedHandler = (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!AssignedAccountLaunchPolicy.IsCurrentView(slot.View, view))
            {
                return;
            }

            _failedAccountIds.Add(accountId);
            SetSlotStatus(slot, "Browser process failed. Use Launch accounts or relaunch this slot to recover it.",
                StatusTone.Error);
        }, DispatcherPriority.Background);
        view.CoreWebView2.ProcessFailed += slot.ProcessFailedHandler;
        RefreshTrackerRows();
    }

    private static void DetachBrowserView(WebView2CompositionControl view)
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

    private async Task CloseAccountViewAsync(Guid accountId, bool preserveFailure = false)
    {
        var slot = _slotCards.FirstOrDefault(item =>
            _panelSettings.SlotAccountIds[item.SlotIndex] == accountId);
        if (slot is not null)
        {
            DetachSlotEventHandlers(slot);
            slot.View = null;
            slot.Placeholder.Visibility = Visibility.Visible;
        }

        await _browserSessions.CloseViewAsync(accountId);
        _openAccountIds.Remove(accountId);
        if (!preserveFailure)
        {
            _failedAccountIds.Remove(accountId);
        }
        _xpTracker.Stop(accountId);
        RefreshTrackerRows();
        _ = SyncLeaderboardParticipationAsync();
        UpdateAllSlotPresentations();
        UpdateManageSlotsButton();
    }

    private async Task CloseAllOpenViewsAsync()
    {
        foreach (var slot in _slotCards)
        {
            DetachSlotEventHandlers(slot);
        }

        foreach (var accountId in _openAccountIds.ToArray())
        {
            await _browserSessions.CloseViewAsync(accountId);
        }

        _openAccountIds.Clear();
        _failedAccountIds.Clear();
        foreach (var accountId in _xpTracker.GetStates().Select(state => state.AccountId).ToArray())
            _xpTracker.Stop(accountId);
        RefreshTrackerRows();
        _ = SyncLeaderboardParticipationAsync();
        foreach (var slot in _slotCards)
        {
            slot.View = null;
            slot.Placeholder.Visibility = Visibility.Visible;
        }

        UpdateAllSlotPresentations();
        UpdateManageSlotsButton();
    }

    private static void DetachSlotEventHandlers(PanelSlotCard slot)
    {
        slot.ViewportUpdateTimer.Stop();
        if (slot.View?.CoreWebView2 is not { } coreWebView)
        {
            return;
        }

        if (slot.NavigationCompletedHandler is not null)
        {
            coreWebView.NavigationCompleted -= slot.NavigationCompletedHandler;
            slot.NavigationCompletedHandler = null;
        }

        if (slot.ProcessFailedHandler is not null)
        {
            coreWebView.ProcessFailed -= slot.ProcessFailedHandler;
            slot.ProcessFailedHandler = null;
        }
    }

    private async Task SaveAccountsAsync()
    {
        await _accountStore.SaveAsync(_accounts.OrderBy(account => account.SortOrder).ToArray());
        _ = SyncLeaderboardParticipationAsync();
    }

    internal LeaderboardCoordinator? Leaderboard => _leaderboard;

    internal async Task SetLeaderboardSharingAsync(bool enabled, CancellationToken ct = default)
    {
        if (_leaderboard is not { } leaderboard) return;
        await leaderboard.SetSharingEnabledAsync(enabled, ct);
        await SyncLeaderboardParticipationAsync();
    }

    private async Task SyncLeaderboardParticipationAsync()
    {
        if (_leaderboard is not { } leaderboard) return;
        try
        {
            var activeProfiles = _xpTracker.GetActiveLeaderboardProfiles()
                .Where(profile => _openAccountIds.Contains(profile.AccountId))
                .ToDictionary(profile => profile.AccountId);
            var profiles = _accounts.Select(account =>
            {
                if (activeProfiles.TryGetValue(account.Id, out var active))
                    return new LeaderboardProfile(active.PlayerId, active.Username);

                return new LeaderboardProfile(account.RankingPlayerId ?? 0,
                    account.RankingUsername?.Trim() ?? string.Empty);
            }).Where(profile => profile.PlayerId > 0 && !string.IsNullOrWhiteSpace(profile.Username))
                .GroupBy(profile => profile.PlayerId)
                .Select(group => group.First()).ToArray();
            var linkedIds = profiles.Select(profile => profile.PlayerId).ToHashSet();
            var activeIds = activeProfiles.Values.Select(profile => profile.PlayerId)
                .Where(linkedIds.Contains).Distinct().ToArray();
            await leaderboard.SyncParticipationAsync(profiles, activeIds, CancellationToken.None);
        }
        catch
        {
            // Participation is optional and must never interrupt view launch or local XP tracking.
        }
    }

    private Task<PanelSettings> UpdateSettingsAsync(Func<PanelSettings, PanelSettings> update) =>
        UpdateSettingsAsync(currentSettings => Task.FromResult(update(currentSettings)));

    private async Task<PanelSettings> UpdateSettingsAsync(
        Func<PanelSettings, Task<PanelSettings>> update,
        Func<PanelSettings, Task>? rollback = null)
    {
        await _settingsMutationGate.WaitAsync();
        try
        {
            var previousSettings = _panelSettings;
            var nextSettings = await update(previousSettings);
            try
            {
                await _settingsStore.SaveAsync(nextSettings);
            }
            catch
            {
                try
                {
                    if (rollback is not null)
                    {
                        await rollback(previousSettings);
                    }
                }
                catch
                {
                    // Preserve the original save failure; the next launch reapplies saved settings.
                }

                throw;
            }

            _panelSettings = nextSettings;
            return nextSettings;
        }
        finally
        {
            _settingsMutationGate.Release();
        }
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsMutationGate.WaitAsync();
        try
        {
            await _settingsStore.SaveAsync(_panelSettings);
        }
        finally
        {
            _settingsMutationGate.Release();
        }
    }

    private async Task ReloadLocalDataAfterFailureAsync()
    {
        try
        {
            _accounts.Clear();
            foreach (var account in await _accountStore.LoadAsync())
            {
                _accounts.Add(account);
            }

            await _settingsMutationGate.WaitAsync();
            try
            {
                _panelSettings = await _settingsStore.LoadAsync();
            }
            finally
            {
                _settingsMutationGate.Release();
            }
            LayoutPicker.SelectedValue = _panelSettings.Layout;
            await RebuildPanelAsync(closeExistingViews: true);
            UpdateAccountActions();
        }
        catch
        {
            // Keep the current window visible if local data itself cannot be loaded.
        }
    }

    private void ReplaceAccount(AccountProfile replacement)
    {
        var index = _accounts.ToList().FindIndex(account => account.Id == replacement.Id);
        if (index >= 0)
        {
            _accounts[index] = replacement;
        }
    }

    private void RestoreAccountOrder(IReadOnlyList<AccountProfile> accounts)
    {
        _accounts.Clear();
        foreach (var account in accounts)
        {
            _accounts.Add(account);
        }
    }

    private void NormalizeSortOrder()
    {
        for (var index = 0; index < _accounts.Count; index++)
        {
            _accounts[index] = _accounts[index] with { SortOrder = index };
        }
    }

    private void UpdateAccountActions()
    {
        var selected = SelectedAccount;
        var hasSelection = selected is not null;
        RenameAccountButton.IsEnabled = hasSelection;
        FavoriteAccountButton.IsEnabled = hasSelection;
        RemoveAccountButton.IsEnabled = hasSelection;
        MoveUpButton.IsEnabled = hasSelection && AccountsListBox.SelectedIndex > 0;
        MoveDownButton.IsEnabled = hasSelection && AccountsListBox.SelectedIndex < _accounts.Count - 1;
        FavoriteAccountButton.Content = selected?.IsFavorite == true ? "Unfavorite" : "Favorite";
        AccountCountText.Text = _accounts.Count == 1 ? "1 profile" : $"{_accounts.Count} profiles";
    }

    private void SetSlotStatus(PanelSlotCard slot, string message, StatusTone tone)
    {
        slot.Tone = tone;
        slot.Status.Text = message;
        slot.Status.Foreground = (Brush)FindResource(tone switch
        {
            StatusTone.Success => "StatusSuccessBrush",
            StatusTone.Warning => "StatusWarningBrush",
            StatusTone.Error => "StatusErrorBrush",
            StatusTone.Info => "Brush.Status.Info",
            _ => "Brush.Status.Neutral"
        });
        UpdateSlotPresentation(slot);
    }

    private static string SafeBrowserError(Exception exception) => exception switch
    {
        WebView2RuntimeNotFoundException => "Microsoft Edge WebView2 Runtime is not installed.",
        UnauthorizedAccessException => "WebView2 could not access its local profile folder.",
        _ => "This account view could not be opened. Other slots remain available."
    };

    private void BrowserSessions_NavigationBlocked(object? sender, NavigationBlockedEventArgs e)
    {
        var slot = _slotCards.FirstOrDefault(item => _panelSettings.SlotAccountIds[item.SlotIndex] == e.AccountId);
        if (slot is not null)
        {
            SetSlotStatus(slot, "Blocked navigation outside FourFold Online.", StatusTone.Warning);
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _leaderboardRefreshTimer.Stop();
        LeaderboardPanelView.Disconnect();
        try
        {
            CancelProfileRead();
            try
            {
                await SaveSettingsAsync();
            }
            catch
            {
                // Closing the window must not leave WebView2 controllers alive.
            }

            try
            {
                await CloseAllOpenViewsAsync();
                await _xpTracker.DisposeAsync();
                if (_leaderboard is { } leaderboard)
                {
                    await SyncLeaderboardParticipationAsync();
                    using var goodbyeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await leaderboard.FlushPendingAsync(goodbyeTimeout.Token); }
                    catch { /* The saved empty heartbeat is retried on next launch. */ }
                    await leaderboard.DisposeAsync();
                }
            }
            finally
            {
                CompleteShutdown();
            }
        }
        catch
        {
            CompleteShutdown();
        }
    }

    private void CompleteShutdown()
    {
        if (_allowClose)
        {
            return;
        }

        var shortcuts = _shortcuts;
        _shortcuts = null;
        try
        {
            shortcuts?.Dispose();
        }
        catch
        {
            // Native hotkey cleanup must not prevent the manager from closing.
        }

        if (_windowSource is { } windowSource)
        {
            _windowSource = null;
            try
            {
                windowSource.RemoveHook(MainWindow_HwndSourceHook);
            }
            catch
            {
                // The source may already be shutting down with its window.
            }
        }

        _allowClose = true;
        Close();
    }

    private static string FormatLayout(PanelLayout layout) => layout switch
    {
        PanelLayout.OneByTwo => "1 × 2",
        PanelLayout.TwoByOne => "2 × 1",
        PanelLayout.TwoByTwo => "2 × 2",
        PanelLayout.TwoByThree => "2 × 3",
        PanelLayout.OneByThree => "1 × 3 · One above three",
        PanelLayout.OneByTwoVertical => "1 × 2 vertical",
        PanelLayout.OneByOne => "1 × 1",
        _ => "Unknown"
    };

    private enum StatusTone { Neutral, Info, Success, Warning, Error }

    private enum AssignedAccountStartResult
    {
        Opened,
        AlreadyRunning,
        ManualActionRequired,
        Failed
    }

    public sealed record LayoutChoice(PanelLayout Layout, string Label)
    {
        public override string ToString() => Label;
    }

    public sealed record SlotAccountChoice(Guid? AccountId, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class PanelSlotCard(
        int slotIndex,
        Border root,
        Grid header,
        Button relaunchButton,
        ComboBox accountPicker,
        TextBlock accountLabel,
        TextBlock status,
        Grid browserHost,
        OverlayCardLayer overlayLayer,
        FrameworkElement placeholder,
        TextBlock emptyTitle,
        TextBlock emptyDescription,
        Border viewAdjustmentOverlay,
        Slider widthSlider,
        Slider heightSlider,
        TextBlock widthValue,
        TextBlock heightValue)
    {
        public int SlotIndex { get; } = slotIndex;
        public Border Root { get; } = root;
        public Grid Header { get; } = header;
        public Button RelaunchButton { get; } = relaunchButton;
        public ComboBox AccountPicker { get; } = accountPicker;
        public TextBlock AccountLabel { get; } = accountLabel;
        public TextBlock Status { get; } = status;
        public Grid BrowserHost { get; } = browserHost;
        public OverlayCardLayer OverlayLayer { get; } = overlayLayer;
        public FrameworkElement Placeholder { get; } = placeholder;
        public TextBlock EmptyTitle { get; } = emptyTitle;
        public TextBlock EmptyDescription { get; } = emptyDescription;
        public Border ViewAdjustmentOverlay { get; } = viewAdjustmentOverlay;
        public Slider WidthSlider { get; } = widthSlider;
        public Slider HeightSlider { get; } = heightSlider;
        public TextBlock WidthValue { get; } = widthValue;
        public TextBlock HeightValue { get; } = heightValue;
        public DispatcherTimer ViewportUpdateTimer { get; } = new() { Interval = TimeSpan.FromMilliseconds(120) };
        public bool SuppressViewportEvents { get; set; }
        public StatusTone Tone { get; set; }
        public WebView2CompositionControl? View { get; set; }
        public EventHandler<CoreWebView2NavigationCompletedEventArgs>? NavigationCompletedHandler { get; set; }
        public EventHandler<CoreWebView2ProcessFailedEventArgs>? ProcessFailedHandler { get; set; }
    }
}
