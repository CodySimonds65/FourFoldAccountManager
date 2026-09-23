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

    public ClassComparisonPanel()
    {
        InitializeComponent();
        RowsControl.ItemsSource = _rows;
    }

    public event EventHandler? RefreshRequested;

    public void SetSnapshot(AccountProfile account, PlayerProgressSnapshot snapshot)
    {
        AccountText.Text = account.Label;
        var state = ClassComparisonDisplayState.FromSnapshot(snapshot);
        ClassText.Text = state.ActiveClassName is null ? "Active class unavailable" :
            $"{state.ActiveClassName} · Level {state.Level}";
        UpdatedText.Text = string.IsNullOrWhiteSpace(state.SourceUpdated)
            ? "Profile timestamp unavailable" : $"Updated {state.SourceUpdated}";
        StatusText.Text = state.Status;
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
        AccountText.Text = string.Empty;
        ClassText.Text = string.Empty;
        UpdatedText.Text = string.Empty;
        StatusText.Text = status;
        AboveText.Text = "—";
        BelowText.Text = "—";
        MeanText.Text = "—";
        _rows.Clear();
        ProfileImage.Source = null;
        ProfileImage.Visibility = Visibility.Collapsed;
    }

    public void SetProfileStatus(string status) => StatusText.Text = status;

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void SetPortrait(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            ProfileImage.Source = null;
            ProfileImage.Visibility = Visibility.Collapsed;
            return;
        }

        var slug = className.Trim().ToLowerInvariant().Replace(' ', '_');
        try
        {
            ProfileImage.Source = new BitmapImage(new Uri(
                $"/FourFoldAccountManager.Desktop;component/Assets/Calculator/classes/{slug}.png",
                UriKind.Relative));
            ProfileImage.Visibility = Visibility.Visible;
        }
        catch (IOException)
        {
            ProfileImage.Source = null;
            ProfileImage.Visibility = Visibility.Collapsed;
        }
    }
}
