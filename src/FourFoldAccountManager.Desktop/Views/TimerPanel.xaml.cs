using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using FourFoldAccountManager.Desktop.Services;

namespace FourFoldAccountManager.Desktop.Views;

public partial class TimerPanel : UserControl
{
    private TimerCoordinator? _coordinator;

    public TimerPanel()
    {
        InitializeComponent();
        IsVisibleChanged += TimerPanel_IsVisibleChanged;
    }

    public void Attach(TimerCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        if (_coordinator is not null)
        {
            throw new InvalidOperationException("The timer panel is already attached to a timer.");
        }

        _coordinator = coordinator;
        DataContext = coordinator.Display;
        coordinator.Display.Laps.CollectionChanged += Laps_CollectionChanged;
    }

    public void SetHotkeys(string splitKeys, string finishKeys, string resetKeys, bool anyUnavailable)
    {
        HotkeyText.Text = $"Split {splitKeys} · Finish {finishKeys} · Reset {resetKeys}";
        UnavailableNote.Visibility = anyUnavailable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SplitButton_Click(object sender, RoutedEventArgs e) => _coordinator?.Split();

    private void FinishButton_Click(object sender, RoutedEventArgs e) => _coordinator?.Finish();

    private void ResetButton_Click(object sender, RoutedEventArgs e) => _coordinator?.Reset();

    // Keep the newest lap in view as laps are recorded.
    private void Laps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 })
        {
            ScrollToLastLap();
        }
    }

    // ScrollIntoView on a collapsed ListBox is a no-op, so laps recorded while the Timer tab was hidden
    // (e.g. running in full screen) would otherwise leave the list scrolled to the oldest lap. Catch up
    // once the panel is actually shown again.
    private void TimerPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            ScrollToLastLap();
        }
    }

    private void ScrollToLastLap()
    {
        if (_coordinator is { } coordinator && coordinator.Display.Laps.Count > 0)
        {
            LapsList.ScrollIntoView(coordinator.Display.Laps[^1]);
        }
    }
}
