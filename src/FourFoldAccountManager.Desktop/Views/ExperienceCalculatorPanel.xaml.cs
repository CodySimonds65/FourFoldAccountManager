using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record ExperienceTransitionRow(string LevelText, string XpText);

public partial class ExperienceCalculatorPanel : UserControl
{
    private readonly ObservableCollection<ExperienceTransitionRow> _transitions = [];
    private ExperienceCalculatorState? _state;
    private bool _updatingTarget;

    public ExperienceCalculatorPanel()
    {
        InitializeComponent();
        TransitionsControl.ItemsSource = _transitions;
    }

    public event EventHandler? RefreshRequested;

    public void SetSnapshot(AccountProfile account, PlayerProgressSnapshot snapshot)
    {
        AccountText.Text = account.Label;
        _state = ExperienceCalculatorState.FromSnapshot(snapshot);
        _updatingTarget = true;
        TargetLevelBox.Text = _state.TargetLevel > 0
            ? _state.TargetLevel.ToString(CultureInfo.InvariantCulture) : string.Empty;
        _updatingTarget = false;
        Render();
    }

    public void ClearSnapshot(string status)
    {
        _state = null;
        AccountText.Text = string.Empty;
        ClassText.Text = string.Empty;
        RemainingText.Text = "—";
        CurrentAbsoluteText.Text = "—";
        TargetAbsoluteText.Text = "—";
        LevelsText.Text = "Levels remaining: —";
        StatusText.Text = status;
        _transitions.Clear();
        TargetLevelBox.Text = string.Empty;
    }

    public void SetProfileStatus(string status) => StatusText.Text = status;

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyTarget();

    private void TargetLevelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingTarget)
        {
            ApplyTarget();
        }
    }

    private void ApplyTarget()
    {
        if (_state?.Profile is null || !long.TryParse(TargetLevelBox.Text.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var targetLevel))
        {
            if (_state?.Profile is not null)
            {
                StatusText.Text = "Enter a positive target level.";
            }

            return;
        }

        _state = _state.WithTarget(targetLevel);
        Render();
    }

    private void Render()
    {
        if (_state is null)
        {
            return;
        }

        ClassText.Text = _state.ClassName is null ? "Active class unavailable" :
            $"{_state.ClassName} · Level {_state.CurrentLevel}";
        RemainingText.Text = _state.RemainingXpText;
        CurrentAbsoluteText.Text = _state.CurrentAbsoluteXpText;
        TargetAbsoluteText.Text = _state.TargetAbsoluteXpText;
        LevelsText.Text = _state.IsValid
            ? $"Levels remaining: {_state.LevelsRemaining.ToString("N0", CultureInfo.CurrentCulture)}"
            : "Levels remaining: —";
        StatusText.Text = _state.Status;
        _transitions.Clear();
        if (_state.Projection is not null)
        {
            foreach (var transition in _state.Projection.Transitions)
            {
                _transitions.Add(new ExperienceTransitionRow(
                    $"Level {transition.FromLevel} → {transition.ToLevel}",
                    transition.XpRequired.ToString("N0", CultureInfo.CurrentCulture) + " XP"));
            }
        }
    }
}
