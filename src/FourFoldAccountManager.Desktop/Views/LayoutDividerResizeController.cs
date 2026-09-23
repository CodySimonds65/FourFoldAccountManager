using System.Windows.Controls;

namespace FourFoldAccountManager.Desktop.Views;

internal sealed class LayoutDividerResizeController
{
    private readonly List<GridSplitter> _splitters = [];
    private readonly HashSet<GridSplitter> _temporarilyDisabled = [];

    public bool IsLocked { get; private set; }

    public bool CanResize => !IsLocked;

    public void Track(GridSplitter splitter)
    {
        ArgumentNullException.ThrowIfNull(splitter);
        _splitters.Add(splitter);
        ApplyEnabledState(splitter);
    }

    public bool Toggle()
    {
        IsLocked = !IsLocked;
        foreach (var splitter in _splitters)
        {
            ApplyEnabledState(splitter);
        }

        return IsLocked;
    }

    public void SetTemporarilyDisabled(GridSplitter splitter, bool isDisabled)
    {
        ArgumentNullException.ThrowIfNull(splitter);
        if (isDisabled)
        {
            _temporarilyDisabled.Add(splitter);
        }
        else
        {
            _temporarilyDisabled.Remove(splitter);
        }

        ApplyEnabledState(splitter);
    }

    public void Reset()
    {
        _splitters.Clear();
        _temporarilyDisabled.Clear();
    }

    private void ApplyEnabledState(GridSplitter splitter) =>
        splitter.IsEnabled = CanResize && !_temporarilyDisabled.Contains(splitter);
}
