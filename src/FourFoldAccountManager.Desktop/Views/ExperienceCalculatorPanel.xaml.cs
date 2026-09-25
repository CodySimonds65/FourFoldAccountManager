using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record ExperienceTransitionRow(string LevelText, string XpText);

public partial class ExperienceCalculatorPanel : UserControl
{
    private readonly ObservableCollection<ExperienceTransitionRow> _transitions = [];
    private ExperienceCalculatorState? _state;
    private Guid? _snapshotAccountId;
    private bool _updatingTarget;
    private bool _suppressAccountSelection;
    private readonly DispatcherTimer _targetSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private (Guid AccountId, long? TargetLevel)? _pendingTargetSave;

    public ExperienceCalculatorPanel()
    {
        InitializeComponent();
        TransitionsControl.ItemsSource = _transitions;
        _targetSaveTimer.Tick += (_, _) => FlushPendingTargetSave();
    }

    public event EventHandler? RefreshRequested;
    public event Action<Guid>? AccountSelectionRequested;

    // Raised once typing pauses; a null level removes the account's saved target.
    public event Action<Guid, long?>? TargetLevelChanged;

    internal TimeSpan TargetSaveDelay => _targetSaveTimer.Interval;

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

    public void SetSnapshot(AccountProfile account, PlayerProgressSnapshot snapshot, long? savedTargetLevel = null)
    {
        var sameAccount = _snapshotAccountId == account.Id;
        if (!sameAccount)
        {
            // Save what was typed for the previous account before its text is replaced.
            FlushPendingTargetSave();
        }

        var targetText = sameAccount
            ? TargetLevelBox.Text
            : savedTargetLevel?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _snapshotAccountId = account.Id;
        SetSelectedAccount(account);
        _state = ExperienceCalculatorState.FromSnapshot(snapshot);
        _updatingTarget = true;
        TargetLevelBox.Text = targetText;
        _updatingTarget = false;
        if (long.TryParse(targetText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture,
                out var targetLevel) && targetLevel > 0)
            _state = _state.WithTarget(targetLevel);
        Render();
    }

    public void ClearSnapshot(string status)
    {
        FlushPendingTargetSave();
        _snapshotAccountId = null;
        _state = null;
        ClassText.Text = string.Empty;
        RemainingText.Text = "—";
        CurrentAbsoluteText.Text = "—";
        TargetAbsoluteText.Text = "—";
        LevelsText.Text = "Levels remaining: —";
        StatusText.Text = status;
        _transitions.Clear();
        _updatingTarget = true;
        TargetLevelBox.Text = string.Empty;
        _updatingTarget = false;
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

    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyTarget();

    private void TargetLevelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingTarget)
        {
            ApplyTarget();
            ScheduleTargetSave();
        }
    }

    private void ApplyTarget()
    {
        if (_state?.Profile is null) return;
        if (!long.TryParse(TargetLevelBox.Text.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var targetLevel) || targetLevel <= 0)
        {
            _state = _state.WithoutTarget();
            Render();
            StatusText.Text = "Enter a positive target level.";
            return;
        }

        _state = _state.WithTarget(targetLevel);
        Render();
    }

    private void ScheduleTargetSave()
    {
        _targetSaveTimer.Stop();
        _pendingTargetSave = null;
        if (_snapshotAccountId is not { } accountId)
        {
            return;
        }

        var text = TargetLevelBox.Text.Trim();
        if (text.Length == 0)
        {
            _pendingTargetSave = (accountId, null);
        }
        else if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var level) &&
                 level > 0 && level <= XpCalculatorTargets.MaxTargetLevel)
        {
            _pendingTargetSave = (accountId, level);
        }
        else
        {
            // Invalid text never changes the saved target.
            return;
        }

        _targetSaveTimer.Start();
    }

    internal void FlushPendingTargetSave()
    {
        _targetSaveTimer.Stop();
        if (_pendingTargetSave is not { } pending)
        {
            return;
        }

        _pendingTargetSave = null;
        TargetLevelChanged?.Invoke(pending.AccountId, pending.TargetLevel);
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
