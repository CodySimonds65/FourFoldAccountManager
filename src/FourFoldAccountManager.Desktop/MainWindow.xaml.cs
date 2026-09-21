using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Panel;
using FourFoldAccountManager.Desktop.Services;
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
    private PanelSettings _panelSettings = PanelSettings.Default;
    private bool _isReady;
    private bool _batchLaunchInProgress;
    private bool _accountsPanelVisible = true;
    private bool _slotManagementVisible = true;
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
        LayoutPicker.IsEnabled = false;
        LaunchVisibleButton.IsEnabled = false;
        LayoutPicker.ItemsSource = new[]
        {
            new LayoutChoice(PanelLayout.OneByTwo, "1 × 2 · Side by side"),
            new LayoutChoice(PanelLayout.TwoByOne, "2 × 1 · Stacked"),
            new LayoutChoice(PanelLayout.TwoByTwo, "2 × 2 · Grid")
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
            _isReady = true;
            AddAccountButton.IsEnabled = true;
            LayoutPicker.IsEnabled = true;
            LaunchVisibleButton.IsEnabled = true;
            LayoutPicker.SelectedValue = _panelSettings.Layout;
            AccountsListBox.SelectedIndex = _accounts.Count > 0 ? 0 : -1;
            UpdateAccountActions();
            await RebuildPanelAsync(closeExistingViews: false);
            GlobalStatusText.Text = "Choose your layout, assign your accounts, and launch your party.";
        }
        catch (InvalidDataException exception)
        {
            AddAccountButton.IsEnabled = false;
            LayoutPicker.IsEnabled = false;
            LaunchVisibleButton.IsEnabled = false;
            GlobalStatusText.Text = "Account data could not be loaded. The original local files were left unchanged.";
            MessageBox.Show(this, exception.Message, "FourFold profile data",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            AddAccountButton.IsEnabled = false;
            LayoutPicker.IsEnabled = false;
            LaunchVisibleButton.IsEnabled = false;
            GlobalStatusText.Text = "The manager could not load local profile data.";
            MessageBox.Show(this, "The local account or panel settings could not be loaded.",
                "FourFold Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        var visibleSlot = Enumerable.Range(0, PanelLayoutPolicy.GetVisibleSlotCount(_panelSettings.Layout))
            .FirstOrDefault(index => _panelSettings.SlotAccountIds[index] is null, -1);
        if (visibleSlot >= 0)
        {
            var nextSettings = PanelLayoutPolicy.Assign(_panelSettings, visibleSlot, account.Id);
            try
            {
                await _settingsStore.SaveAsync(nextSettings);
                _panelSettings = nextSettings;
            }
            catch
            {
                await RefreshSlotPickersAsync();
                GlobalStatusText.Text = $"Added {account.Label}, but its panel assignment could not be saved. Choose it from a slot menu.";
                return;
            }
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
            var nextSettings = PanelLayoutPolicy.ClearAccount(_panelSettings, account.Id);
            await _settingsStore.SaveAsync(nextSettings);
            _panelSettings = nextSettings;
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

        var nextSettings = PanelLayoutPolicy.WithLayout(_panelSettings, layout);
        try
        {
            await _settingsStore.SaveAsync(nextSettings);
            _panelSettings = nextSettings;
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
        FullScreenExitButton.Opacity = 0.95;

    private void FullScreenExit_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        FullScreenExitButton.Opacity = 0.35;

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
        FullScreenExitButton.Visibility = Visibility.Visible;
        UpdateAllSlotPresentations();

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

        WindowState = _previousWindowState;
    }

    private void UpdateManageSlotsButton()
    {
        ManageSlotsButton.Visibility = _openAccountIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ManageSlotsButton.Content = _slotManagementVisible ? "Done" : "Manage slots";
        ManageSlotsButton.ToolTip = _slotManagementVisible
            ? "Hide account selectors for started views."
            : "Show account selectors to change slot assignments.";
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
            var manual = outcomes.Count(result => result == AssignedAccountStartResult.ManualSignInRequired);
            var failed = outcomes.Count(result => result == AssignedAccountStartResult.Failed);
            GlobalStatusText.Text = $"Start complete: {opened} browser view(s) opened, {manual} need manual sign-in, {failed} failed.";
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
            return AssignedAccountStartResult.ManualSignInRequired;
        }

        if (credentials is null)
        {
            SetSlotStatus(slot, "Sign-in page ready. Add a saved login or sign in manually in this slot.", StatusTone.Warning);
            return AssignedAccountStartResult.ManualSignInRequired;
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

        SetSlotStatus(slot, "Browser game page opened. If FourFold did not accept the login, sign in manually here.",
            StatusTone.Success);
        return AssignedAccountStartResult.Opened;
    }

    private void SetBatchLaunchMode(bool isActive)
    {
        _batchLaunchInProgress = isActive;
        LaunchVisibleButton.IsEnabled = !isActive && _isReady;
        LaunchVisibleButton.Content = isActive ? "Launching…" : "Launch accounts";
        LayoutPicker.IsEnabled = !isActive && _isReady;
        AddAccountButton.IsEnabled = !isActive && _isReady;
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

        var nextSettings = PanelLayoutPolicy.Assign(_panelSettings, slotIndex, choice.AccountId);
        try
        {
            await _settingsStore.SaveAsync(nextSettings);
            _panelSettings = nextSettings;
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

        var dimensions = PanelLayoutPolicy.GetDimensions(_panelSettings.Layout);
        for (var row = 0; row < dimensions.Rows; row++)
        {
            PanelGridHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var column = 0; column < dimensions.Columns; column++)
        {
            PanelGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var slotCount = PanelLayoutPolicy.GetVisibleSlotCount(_panelSettings.Layout);
        for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            var card = CreatePanelSlot(slotIndex);
            Grid.SetRow(card.Root, slotIndex / dimensions.Columns);
            Grid.SetColumn(card.Root, slotIndex % dimensions.Columns);
            PanelGridHost.Children.Add(card.Root);
            _slotCards.Add(card);
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
        Grid.SetRow(browserHost, 2);
        content.Children.Add(browserHost);
        var slot = new PanelSlotCard(slotIndex, root, header, accountPicker, accountLabel, status, browserHost,
            placeholder, emptyTitle, emptyDescription);
        UpdateSlotPresentation(slot);
        return slot;
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

    private void AttachBrowserView(PanelSlotCard slot, WebView2 view)
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

    private static void DetachBrowserView(WebView2 view)
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

    private Task SaveSettingsAsync() => _settingsStore.SaveAsync(_panelSettings);

    private async Task ReloadLocalDataAfterFailureAsync()
    {
        try
        {
            _accounts.Clear();
            foreach (var account in await _accountStore.LoadAsync())
            {
                _accounts.Add(account);
            }

            _panelSettings = await _settingsStore.LoadAsync();
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
        _ => "Unknown"
    };

    private enum StatusTone { Neutral, Info, Success, Warning, Error }

    private enum AssignedAccountStartResult
    {
        Opened,
        ManualSignInRequired,
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
        TextBlock emptyDescription)
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
        public StatusTone Tone { get; set; }
        public WebView2? View { get; set; }
        public EventHandler<CoreWebView2NavigationCompletedEventArgs>? NavigationCompletedHandler { get; set; }
        public EventHandler<CoreWebView2ProcessFailedEventArgs>? ProcessFailedHandler { get; set; }
    }
}
