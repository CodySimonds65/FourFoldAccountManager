using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Desktop.Services;

namespace FourFoldAccountManager.Desktop.Views;

public partial class LeaderboardPanel : UserControl
{
    private const int PageSize = 25;
    private LeaderboardCoordinator? _coordinator;
    private Func<bool, Task>? _setSharing;
    private CancellationTokenSource? _refreshCancellation;
    private LeaderboardPeriod _period = LeaderboardPeriod.Daily;
    private LeaderboardPage? _page;
    private int _pageNumber = 1;
    private int _refreshVersion;
    private bool _visible;
    private bool _configured;
    private bool _changingSharing;
    private bool _showingCachedData;

    public LeaderboardPanel()
    {
        InitializeComponent();
        UpdatePeriodButtons();
        UpdatePeriodWindow();
        UpdatePaging();
        StateText.Text = "Open the leaderboard to load rankings.";
    }

    public void Configure(LeaderboardCoordinator? coordinator, Func<bool, Task> setSharing)
    {
        if (_coordinator is not null) _coordinator.StatusChanged -= Coordinator_StatusChanged;
        _coordinator = coordinator;
        _setSharing = setSharing;
        _configured = coordinator is not null;
        if (coordinator is not null) coordinator.StatusChanged += Coordinator_StatusChanged;
        _changingSharing = true;
        ShareLinkedAccountsCheckBox.IsChecked = coordinator?.SharingEnabled == true;
        _changingSharing = false;
        ShareLinkedAccountsCheckBox.IsEnabled = _configured;
        StateText.Text = coordinator is null
            ? "Leaderboard settings could not be loaded on this PC."
            : coordinator.IsConfigured
                ? "Choose a period to see rankings."
                : "The shared leaderboard service is not configured on this PC.";
        if (_visible) _ = RefreshAsync();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (visible) _ = RefreshAsync();
        else Stop();
    }

    public void Stop()
    {
        _visible = false;
        _refreshVersion++;
        _refreshCancellation?.Cancel();
    }

    public void Disconnect()
    {
        Stop();
        if (_coordinator is not null) _coordinator.StatusChanged -= Coordinator_StatusChanged;
    }

    public async Task RefreshAsync()
    {
        if (!_visible || _coordinator is null) return;
        _refreshCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        var version = ++_refreshVersion;
        var period = _period;
        var pageNumber = _pageNumber;
        var coordinator = _coordinator;
        RefreshButton.IsEnabled = false;
        StateText.Text = _page is null ? "Loading rankings…" : "Updating rankings…";
        UpdatePeriodWindow();

        try
        {
            var cached = await coordinator.GetCachedPageAsync(period, pageNumber, PageSize,
                cancellation.Token);
            if (!IsCurrent(version, cancellation)) return;
            _page = cached;
            _showingCachedData = cached is not null;
            if (cached is not null)
            {
                ShowPage(cached);
                if (coordinator.IsConfigured) StateText.Text = "Checking for new rankings…";
            }
            else
            {
                RowsControl.ItemsSource = null;
                FreshnessText.Text = "No saved snapshot is available.";
                StateText.Text = coordinator.IsConfigured
                    ? "Loading rankings…"
                    : "The shared leaderboard service is not configured on this PC.";
                UpdatePaging();
            }

            if (!coordinator.IsConfigured) return;
            // The view timer performs retries while visible; this request must not create
            // a coordinator retry that survives a switch back to Workspace.
            var fresh = await coordinator.RefreshPageAsync(period, pageNumber, PageSize,
                cancellation.Token, scheduleRetry: false);
            if (!IsCurrent(version, cancellation)) return;
            if (fresh is not null)
            {
                _page = fresh;
                _showingCachedData = false;
                ShowPage(fresh);
            }
            else if (cached is not null)
            {
                _showingCachedData = true;
                ShowPage(cached);
            }
            else
            {
                StateText.Text = "The service is unavailable and no rankings have been saved yet.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (IsCurrent(version, cancellation))
                StateText.Text = _page is null
                    ? "Rankings could not be loaded. Try again later."
                    : "Rankings could not be refreshed. Showing the last loaded page.";
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, cancellation))
            {
                _refreshCancellation = null;
                RefreshButton.IsEnabled = true;
            }
            cancellation.Dispose();
        }
    }

    private bool IsCurrent(int version, CancellationTokenSource cancellation) =>
        _visible && !cancellation.IsCancellationRequested && version == _refreshVersion;

    private void ShowPage(LeaderboardPage page)
    {
        RowsControl.ItemsSource = page.Entries.Select(entry => new LeaderboardRow(
            entry.Rank.ToString(CultureInfo.CurrentCulture), entry.Username,
            entry.XpGained.ToString("N0", CultureInfo.CurrentCulture),
            entry.LastSampledAtUtc.UtcDateTime.ToString("MMM d, HH:mm 'UTC'", CultureInfo.InvariantCulture),
            entry.IsStale ? "Sample is stale" : "Fresh sample")).ToArray();

        var offline = !_coordinator!.IsConfigured || _showingCachedData;
        FreshnessText.Text = offline
            ? $"Saved snapshot · last updated {FormatUtc(page.GeneratedAtUtc)}"
            : $"Updated {FormatUtc(page.GeneratedAtUtc)}";

        var currentWindow = LeaderboardPeriodWindow.GetCurrent(_period, DateTimeOffset.UtcNow);
        if (offline &&
            (page.PeriodStartUtc != currentWindow.StartUtc || page.PeriodEndUtc != currentWindow.EndUtc))
        {
            StateText.Text = "This saved snapshot is from a previous UTC period.";
        }
        else if (offline)
        {
            StateText.Text = !_coordinator.IsConfigured
                ? "The shared leaderboard service is not configured on this PC."
                : "The service is unavailable. Showing saved rankings.";
        }
        else
        {
            StateText.Text = page.Entries.Count == 0
                ? "No XP gains have been recorded for this period yet."
                : string.Empty;
        }
        UpdatePaging();
    }

    private void UpdatePeriodWindow()
    {
        var (start, end) = LeaderboardPeriodWindow.GetCurrent(_period, DateTimeOffset.UtcNow);
        PeriodWindowText.Text = $"Current {_period.ToString().ToLowerInvariant()} period: " +
                                $"{FormatUtc(start)} to {FormatUtc(end)} (end exclusive)";
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("MMM d, yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private void UpdatePaging()
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling((_page?.TotalEntries ?? 0) / (double)PageSize));
        PageText.Text = $"Page {_pageNumber} of {Math.Max(_pageNumber, totalPages)}";
        PreviousButton.IsEnabled = _pageNumber > 1;
        NextButton.IsEnabled = _page is not null && _pageNumber < totalPages;
    }

    private void UpdatePeriodButtons()
    {
        foreach (var (button, period) in new[]
                 {
                     (DailyButton, LeaderboardPeriod.Daily),
                     (WeeklyButton, LeaderboardPeriod.Weekly),
                     (MonthlyButton, LeaderboardPeriod.Monthly)
                 })
        {
            var selected = _period == period;
            button.Background = (Brush)FindResource(selected ? "Brush.AccentSoft" : "Brush.SurfaceRaised");
            button.Foreground = (Brush)FindResource(selected ? "Brush.AccentGold" : "Brush.TextPrimary");
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void Coordinator_StatusChanged(object? sender, LeaderboardStatus status)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => Coordinator_StatusChanged(sender, status));
            return;
        }
        if (_visible && _page is not null && !status.IsOnline)
        {
            _showingCachedData = true;
            ShowPage(_page);
        }
    }

    private void Period_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<LeaderboardPeriod>(tag, out var period) || period == _period) return;
        _period = period;
        _pageNumber = 1;
        _page = null;
        RowsControl.ItemsSource = null;
        UpdatePeriodButtons();
        UpdatePaging();
        _ = RefreshAsync();
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => GoToPage(_pageNumber - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => GoToPage(_pageNumber + 1);

    private void GoToPage(int page)
    {
        if (page < 1) return;
        _pageNumber = page;
        _page = null;
        RowsControl.ItemsSource = null;
        UpdatePaging();
        _ = RefreshAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private async void SharingChanged(object sender, RoutedEventArgs e)
    {
        if (_changingSharing || !_configured || _setSharing is null || _coordinator is null) return;
        var enabled = ShareLinkedAccountsCheckBox.IsChecked == true;
        ShareLinkedAccountsCheckBox.IsEnabled = false;
        try
        {
            await _setSharing(enabled);
            StateText.Text = enabled
                ? "Sharing enabled for linked accounts. Active game views can contribute XP gains."
                : "Sharing disabled for linked accounts.";
        }
        catch
        {
            _changingSharing = true;
            ShareLinkedAccountsCheckBox.IsChecked = _coordinator.SharingEnabled;
            _changingSharing = false;
            StateText.Text = "Could not save the sharing preference. Try again.";
        }
        finally { ShareLinkedAccountsCheckBox.IsEnabled = true; }
    }

    private sealed record LeaderboardRow(string Rank, string Username, string XpGained,
        string LastSampled, string SampleStatus);
}
