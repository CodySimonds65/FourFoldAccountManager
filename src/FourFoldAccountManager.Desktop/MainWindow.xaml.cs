using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Panel;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Updates;
using FourFoldAccountManager.Desktop.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FourFoldAccountManager.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AccountProfile> _accounts = [];
    private readonly HashSet<Guid> _openAccountIds = [];
    private readonly AccountStore _accountStore;
    private readonly SettingsStore _settingsStore;
    private readonly WindowsCredentialStore _credentialStore;
    private readonly AccountBrowserSessionService _browserSessions;
    private readonly List<PanelSlotCard> _slotCards = [];
    private readonly SemaphoreSlim _settingsMutationGate = new(1, 1);
    private readonly SemaphoreSlim _viewportSaveGate = new(1, 1);
    private PanelSettings _panelSettings = PanelSettings.Default;
    private bool _isReady;
    private bool _batchLaunchInProgress;
    private bool _accountsPanelVisible = true;
    private bool _slotManagementVisible = true;
    private bool _viewAdjustmentVisible;
    private bool _isFullScreen;
    private WindowState _previousWindowState;
    private WindowStyle _previousWindowStyle;
    private ResizeMode _previousResizeMode;
    private bool _shutdownStarted;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);

        var paths = new LocalDataPaths();
        _accountStore = new AccountStore(paths);
        _settingsStore = new SettingsStore(paths);
        _credentialStore = new WindowsCredentialStore();
        _browserSessions = new AccountBrowserSessionService(paths);
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
            new LayoutChoice(PanelLayout.OneByTwoVertical, "1 × 2 · Vertical split")
        };

        LayoutPicker.SelectedValuePath = nameof(LayoutChoice.Layout);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
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
        var dialog = new AddAccountDialog("Add account", "Set a profile label and optional saved login.")
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var account = AccountProfile.Create(dialog.AccountLabel, _accounts.Count);
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
            storedCredentials?.Password ?? string.Empty)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var replacement = account with { Label = dialog.AccountLabel };
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

    private void AccountsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAccountActions();

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
        _accountsPanelVisible = !_accountsPanelVisible;
        AccountsPanel.Visibility = _accountsPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        AccountsColumn.Width = _accountsPanelVisible ? new GridLength(232) : new GridLength(0);
        AccountsGapColumn.Width = _accountsPanelVisible ? new GridLength(16) : new GridLength(0);

        ToggleAccountsButton.ToolTip = _accountsPanelVisible
            ? "Hide the accounts rail to expand the multi-box panel."
            : "Show account profiles and slot assignments.";
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

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(
            _panelSettings.FillGameToPanel,
            _panelSettings.ShowFullScreenExitButton)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true ||
            (dialog.FillGameToPanel == _panelSettings.FillGameToPanel &&
             dialog.ShowFullScreenExitButton == _panelSettings.ShowFullScreenExitButton))
        {
            return;
        }

        PanelSettings? nextSettings = null;
        var scalingChanged = false;
        SettingsButton.IsEnabled = false;
        try
        {
            nextSettings = await UpdateSettingsAsync(async currentSettings =>
            {
                var candidate = currentSettings with
                {
                    FillGameToPanel = dialog.FillGameToPanel,
                    ShowFullScreenExitButton = dialog.ShowFullScreenExitButton
                };
                scalingChanged = candidate.FillGameToPanel != currentSettings.FillGameToPanel;
                if (scalingChanged)
                {
                    await _browserSessions.SetGameScalingAsync(candidate.FillGameToPanel);
                }

                return candidate;
            }, previousSettings => scalingChanged
                ? _browserSessions.SetGameScalingAsync(previousSettings.FillGameToPanel)
                : Task.CompletedTask);
            if (!nextSettings.FillGameToPanel)
            {
                _viewAdjustmentVisible = false;
                UpdateAllSlotPresentations();
            }
            UpdateManageSlotsButton();
            GlobalStatusText.Text = scalingChanged
                ? nextSettings.FillGameToPanel
                    ? "Game scaling set to Fill panel."
                    : "Game scaling set to Fit entire game."
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

        _previousWindowState = WindowState;
        _previousWindowStyle = WindowStyle;
        _previousResizeMode = ResizeMode;
        _isFullScreen = true;

        AppHeaderBorder.Visibility = Visibility.Collapsed;
        AppHeaderRow.Height = new GridLength(0);
        MainContentGrid.Margin = new Thickness(0);
        AccountsPanel.Visibility = Visibility.Collapsed;
        AccountsColumn.Width = new GridLength(0);
        AccountsGapColumn.Width = new GridLength(0);
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

        _isFullScreen = false;
        AppHeaderBorder.Visibility = Visibility.Visible;
        AppHeaderRow.Height = new GridLength(60);
        MainContentGrid.Margin = new Thickness(16);
        AccountsPanel.Visibility = _accountsPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        AccountsColumn.Width = _accountsPanelVisible ? new GridLength(232) : new GridLength(0);
        AccountsGapColumn.Width = _accountsPanelVisible ? new GridLength(16) : new GridLength(0);
        PanelToolbar.Visibility = Visibility.Visible;
        GlobalStatusText.Visibility = Visibility.Visible;
        PanelBorder.Padding = new Thickness(0);
        PanelBorder.CornerRadius = new CornerRadius(0);
        PanelBorder.BorderThickness = new Thickness(0);
        FullScreenExitButton.Visibility = Visibility.Collapsed;
        UpdateAllSlotPresentations();
        UpdateManageSlotsButton();

        WindowState = _previousWindowState;
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
        var account = assignedAccountId is { } id ? _accounts.FirstOrDefault(item => item.Id == id) : null;
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

                GlobalStatusText.Text = $"Starting account in slot {slot.SlotIndex + 1} of {assignedSlots.Length}…";
                try
                {
                    outcomes.Add(await StartAssignedAccountAsync(slot, accountId));
                }
                catch (Exception exception)
                {
                    SetSlotStatus(slot, SafeBrowserError(exception), StatusTone.Error);
                    outcomes.Add(AssignedAccountStartResult.Failed);
                }
            }

            var opened = outcomes.Count(result => result == AssignedAccountStartResult.Opened);
            var manual = outcomes.Count(result => result == AssignedAccountStartResult.ManualActionRequired);
            var failed = outcomes.Count(result => result == AssignedAccountStartResult.Failed);
            GlobalStatusText.Text = $"Start complete: {opened} game(s) opened, {manual} need manual action, {failed} failed.";
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
        AttachBrowserView(slot, view);

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

        _slotCards.Clear();
        PanelGridHost.Children.Clear();
        PanelGridHost.RowDefinitions.Clear();
        PanelGridHost.ColumnDefinitions.Clear();

        var layout = _panelSettings.Layout;
        var dimensions = PanelLayoutPolicy.GetDimensions(layout);
        var hasAdjustableRowSplit = layout == PanelLayout.TwoByThree;
        RowDefinition? topRow = null;
        RowDefinition? bottomRow = null;

        if (hasAdjustableRowSplit)
        {
            var topFraction = _panelSettings.TwoByThreeTopRowFraction;
            topRow = new RowDefinition
            {
                Height = new GridLength(topFraction, GridUnitType.Star),
                MinHeight = 120
            };
            bottomRow = new RowDefinition
            {
                Height = new GridLength(1 - topFraction, GridUnitType.Star),
                MinHeight = 120
            };
            PanelGridHost.RowDefinitions.Add(topRow);
            PanelGridHost.RowDefinitions.Add(bottomRow);

            var splitter = new GridSplitter
            {
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.CurrentAndNext,
                ShowsPreview = false,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.SizeNS,
                Margin = new Thickness(4, 0, 4, -5)
            };
            Panel.SetZIndex(splitter, 100);
            Grid.SetColumnSpan(splitter, dimensions.Columns);
            splitter.DragCompleted += async (_, _) =>
            {
                if (!_isReady || _panelSettings.Layout != PanelLayout.TwoByThree ||
                    topRow.ActualHeight + bottomRow.ActualHeight <= 0)
                {
                    return;
                }

                var nextFraction = Math.Clamp(
                    topRow.ActualHeight / (topRow.ActualHeight + bottomRow.ActualHeight),
                    0.2,
                    0.8);
                topRow.Height = new GridLength(nextFraction, GridUnitType.Star);
                bottomRow.Height = new GridLength(1 - nextFraction, GridUnitType.Star);
                if (Math.Abs(nextFraction - _panelSettings.TwoByThreeTopRowFraction) < 0.001)
                {
                    return;
                }

                try
                {
                    await UpdateSettingsAsync(currentSettings =>
                        currentSettings with { TwoByThreeTopRowFraction = nextFraction });
                    GlobalStatusText.Text = "The 2 × 3 row heights were saved.";
                }
                catch
                {
                    var savedFraction = _panelSettings.TwoByThreeTopRowFraction;
                    topRow.Height = new GridLength(savedFraction, GridUnitType.Star);
                    bottomRow.Height = new GridLength(1 - savedFraction, GridUnitType.Star);
                    MessageBox.Show(this, "The row heights could not be saved.", "FourFold Account Manager",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            Grid.SetRow(splitter, 0);
            PanelGridHost.Children.Add(splitter);
        }
        else
        {
            for (var row = 0; row < dimensions.Rows; row++)
            {
                PanelGridHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }
        }

        for (var column = 0; column < dimensions.Columns; column++)
        {
            PanelGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var slotIndex = 0;
        foreach (var placement in PanelLayoutPolicy.GetSlotPlacements(layout))
        {
            var card = CreatePanelSlot(slotIndex);
            Grid.SetRow(card.Root, placement.Row);
            Grid.SetColumn(card.Root, placement.Column);
            Grid.SetRowSpan(card.Root, placement.RowSpan);
            Grid.SetColumnSpan(card.Root, placement.ColumnSpan);
            PanelGridHost.Children.Add(card.Root);
            _slotCards.Add(card);
            slotIndex++;
        }

        if (!closeExistingViews)
        {
            await RestoreVisibleOpenViewsAsync();
        }

        UpdateManageSlotsButton();
        UpdateAllSlotPresentations();
    }

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
                AttachBrowserView(slot, view);
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
        var slot = new PanelSlotCard(slotIndex, root, header, accountPicker, accountLabel, status, browserHost,
            placeholder, emptyTitle, emptyDescription, adjustmentOverlay, widthSlider, heightSlider,
            widthValue, heightValue);
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

    private void AttachBrowserView(PanelSlotCard slot, WebView2CompositionControl view)
    {
        if (!ReferenceEquals(view.Parent, slot.BrowserHost))
        {
            DetachBrowserView(view);
            slot.BrowserHost.Children.Add(view);
        }

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
                    SetSlotStatus(slot, "Page could not be loaded. Press Launch accounts to retry.", StatusTone.Error);
                }
            }, DispatcherPriority.Background);
        };
        view.CoreWebView2.NavigationCompleted += slot.NavigationCompletedHandler;

        if (slot.ProcessFailedHandler is not null)
        {
            view.CoreWebView2.ProcessFailed -= slot.ProcessFailedHandler;
        }

        slot.ProcessFailedHandler = (_, _) => Dispatcher.BeginInvoke(
            () => SetSlotStatus(slot, "Browser process failed. Other slots remain available.", StatusTone.Error),
            DispatcherPriority.Background);
        view.CoreWebView2.ProcessFailed += slot.ProcessFailedHandler;
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

    private async Task CloseAccountViewAsync(Guid accountId)
    {
        var slot = _slotCards.FirstOrDefault(item =>
            _panelSettings.SlotAccountIds[item.SlotIndex] == accountId);
        if (slot is not null)
        {
            DetachSlotEventHandlers(slot);
        }

        await _browserSessions.CloseViewAsync(accountId);
        _openAccountIds.Remove(accountId);
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

    private async Task SaveAccountsAsync() =>
        await _accountStore.SaveAsync(_accounts.OrderBy(account => account.SortOrder).ToArray());

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
        try
        {
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
            }
            finally
            {
                _allowClose = true;
                Close();
            }
        }
        catch
        {
            _allowClose = true;
            Close();
        }
    }

    private static string FormatLayout(PanelLayout layout) => layout switch
    {
        PanelLayout.OneByTwo => "1 × 2",
        PanelLayout.TwoByOne => "2 × 1",
        PanelLayout.TwoByTwo => "2 × 2",
        PanelLayout.TwoByThree => "2 × 3",
        PanelLayout.OneByTwoVertical => "1 × 2 vertical",
        _ => "Unknown"
    };

    private enum StatusTone { Neutral, Info, Success, Warning, Error }

    private enum AssignedAccountStartResult
    {
        Opened,
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
        ComboBox accountPicker,
        TextBlock accountLabel,
        TextBlock status,
        Grid browserHost,
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
        public ComboBox AccountPicker { get; } = accountPicker;
        public TextBlock AccountLabel { get; } = accountLabel;
        public TextBlock Status { get; } = status;
        public Grid BrowserHost { get; } = browserHost;
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
