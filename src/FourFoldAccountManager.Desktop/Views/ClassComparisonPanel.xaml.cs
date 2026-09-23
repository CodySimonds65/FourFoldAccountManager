using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Views;

public partial class ClassComparisonPanel : UserControl
{
    private static readonly IReadOnlyDictionary<CharacterStat, string> Labels = new Dictionary<CharacterStat, string>
    {
        [CharacterStat.Hp] = "HP",
        [CharacterStat.Sp] = "SP",
        [CharacterStat.Attack] = "ATT",
        [CharacterStat.Magic] = "MAG",
        [CharacterStat.Skill] = "SKL",
        [CharacterStat.Speed] = "SPD",
        [CharacterStat.Luck] = "LCK",
        [CharacterStat.Defense] = "DEF",
        [CharacterStat.Resistance] = "RES"
    };

    private readonly ObservableCollection<ClassComparisonRow> _rows = [];
    private bool _suppressAccountSelection;

    public ClassComparisonPanel()
    {
        InitializeComponent();
        RowsControl.ItemsSource = _rows;
    }

    public event EventHandler? RefreshRequested;
    public event Action<Guid>? AccountSelectionRequested;

    public void SetAccounts(IEnumerable<AccountProfile> accounts) => AccountPicker.ItemsSource = accounts;

    public void SetSelectedAccount(AccountProfile? account)
    {
        _suppressAccountSelection = true;
        try
        {
            AccountPicker.SelectedValue = account?.Id;
        }
        finally
        {
            _suppressAccountSelection = false;
        }
    }

    public void SetSnapshot(AccountProfile account, PlayerProgressSnapshot snapshot)
    {
        SetSelectedAccount(account);
        var state = ClassComparisonDisplayState.FromSnapshot(snapshot);
        ClassText.Text = state.ActiveClassName is null ? "Active class unavailable" :
            $"{state.ActiveClassName} · Level {state.Level}";
        UpdatedText.Text = string.IsNullOrWhiteSpace(state.SourceUpdated)
            ? "Profile timestamp unavailable" : $"Updated {state.SourceUpdated}";
        StatusText.Text = state.Status;
        var activeProfile = state.ActiveClassName is null ? null : snapshot.Classes.FirstOrDefault(pair =>
            string.Equals(pair.Key, state.ActiveClassName, StringComparison.OrdinalIgnoreCase)).Value;
        EquipmentText.Text = activeProfile?.Equipment is { Count: > 0 } equipment
            ? $"Equipment context: {string.Join(" · ", equipment.Select(item => $"{item.Key}: {item.Value}"))}"
            : "Equipment context: not listed; comparison uses displayed base stats only.";
        _rows.Clear();
        foreach (var row in state.Rows)
        {
            _rows.Add(new ClassComparisonRow(
                Labels[row.Stat],
                row.ProfileValue.ToString("N0", CultureInfo.CurrentCulture),
                row.Average.ToString("N0", CultureInfo.CurrentCulture),
                row.Difference.ToString("+#,##0;-#,##0;0", CultureInfo.CurrentCulture),
                row.PercentageDifference is { } percentage
                    ? percentage.ToString("+#,##0.0;-#,##0.0;0.0", CultureInfo.CurrentCulture) + "%"
                    : "—",
                row.Direction));
        }

        AboveText.Text = state.IsAvailable ? state.AboveCount.ToString(CultureInfo.CurrentCulture) : "—";
        BelowText.Text = state.IsAvailable ? state.BelowCount.ToString(CultureInfo.CurrentCulture) : "—";
        MeanText.Text = state.MeanPercentageDifference is { } mean
            ? mean.ToString("+#,##0.0;-#,##0.0;0.0", CultureInfo.CurrentCulture) + "%"
            : "—";
        SetPortrait(state.ActiveClassName);
    }

    public void ClearSnapshot(string status)
    {
        ClassText.Text = string.Empty;
        UpdatedText.Text = string.Empty;
        StatusText.Text = status;
        EquipmentText.Text = string.Empty;
        AboveText.Text = "—";
        BelowText.Text = "—";
        MeanText.Text = "—";
        _rows.Clear();
        ProfileImage.Source = null;
        ProfileImage.Visibility = Visibility.Collapsed;
        ProfileMonogram.Text = "??";
        ProfileMonogram.Visibility = Visibility.Visible;
    }

    public void SetProfileStatus(string status) => StatusText.Text = status;

    private void AccountPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressAccountSelection && AccountPicker.SelectedItem is AccountProfile account)
        {
            AccountSelectionRequested?.Invoke(account.Id);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void SetPortrait(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            ProfileImage.Source = null;
            ProfileImage.Visibility = Visibility.Collapsed;
            ProfileMonogram.Text = "??";
            ProfileMonogram.Visibility = Visibility.Visible;
            return;
        }

        ProfileMonogram.Text = BuildMonogram(className);
        ProfileMonogram.Visibility = Visibility.Visible;
        var slug = className.Trim().ToLowerInvariant().Replace(' ', '_');
        try
        {
            ProfileImage.Source = new BitmapImage(new Uri(
                $"/FourFoldAccountManager.Desktop;component/Assets/Calculator/classes/{slug}.png",
                UriKind.Relative));
            ProfileImage.Visibility = Visibility.Visible;
            ProfileMonogram.Visibility = Visibility.Collapsed;
        }
        catch (IOException)
        {
            ProfileImage.Source = null;
            ProfileImage.Visibility = Visibility.Collapsed;
        }
    }

    private static string BuildMonogram(string className)
    {
        var words = className.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2
            ? string.Concat(words[0][0], words[1][0]).ToUpperInvariant()
            : className.Trim().Length >= 2
                ? className.Trim()[..2].ToUpperInvariant()
                : className.Trim().ToUpperInvariant();
    }
}
