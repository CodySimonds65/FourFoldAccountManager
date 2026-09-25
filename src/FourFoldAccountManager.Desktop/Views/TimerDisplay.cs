using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FourFoldAccountManager.Core.Timing;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record TimerLapRow(int Number, string LapTimeText, string SplitTotalText);

// Live timer text shared by the Timer tab and the full-screen Timer card; ticks update it in place,
// so neither the overlay layers nor the Overlays panel are rebuilt while a run is timing.
public sealed class TimerDisplay : INotifyPropertyChanged, IOverlayCardData
{
    private string _totalText = TimerFormat.Format(TimeSpan.Zero);
    private string _lapText = FormatLapLine(1, TimeSpan.Zero);
    private SpeedrunTimerState _state = SpeedrunTimerState.Ready;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TotalText
    {
        get => _totalText;
        private set => Set(ref _totalText, value);
    }

    public string LapText
    {
        get => _lapText;
        private set => Set(ref _lapText, value);
    }

    public SpeedrunTimerState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(SplitButtonText));
            }
        }
    }

    public string SplitButtonText => State == SpeedrunTimerState.Ready ? "Start" : "Split";

    public ObservableCollection<TimerLapRow> Laps { get; } = [];

    // The Overlays panel detail does not tick, so the Timer card shows no summary there.
    public string Summary => string.Empty;

    internal void Apply(TimerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TotalText = TimerFormat.Format(snapshot.Total);
        LapText = FormatLapLine(snapshot.CurrentLapNumber, snapshot.CurrentLapTime);
        State = snapshot.State;
        if (snapshot.Laps.Count < Laps.Count)
        {
            Laps.Clear();
        }

        for (var index = Laps.Count; index < snapshot.Laps.Count; index++)
        {
            var lap = snapshot.Laps[index];
            Laps.Add(new TimerLapRow(lap.Number, TimerFormat.Format(lap.LapTime), TimerFormat.Format(lap.SplitTotal)));
        }
    }

    private static string FormatLapLine(int number, TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture, $"Lap {number}  {TimerFormat.Format(time)}");

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
