using System.Windows.Controls;
using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public partial class TimerSettingsPage : UserControl
{
    private readonly IReadOnlyDictionary<GlobalShortcutAction, ShortcutRow> _rows;
    private readonly Dictionary<GlobalShortcutAction, string> _keysText = [];
    private GlobalShortcutAction? _capturing;

    public TimerSettingsPage()
    {
        InitializeComponent();
        _rows = new[] { SplitRow, FinishRow, ResetRow }.ToDictionary(row => row.Action);
        foreach (var (action, row) in _rows)
        {
            row.CaptureRequested += (_, _) => BeginCapture(action);
        }
    }

    // MainWindow checks it for clashes, registers it and saves it, then calls ShowKeys again.
    public event Action<GlobalShortcutAction, GlobalHotkeyChord>? ChangeRequested;

    public bool IsCapturing => _capturing is not null;

    public void ShowKeys(GlobalShortcutAction action, string keysText, string status)
    {
        _keysText[action] = keysText;
        if (_capturing != action)
        {
            _rows[action].SetKeysText(keysText);
            _rows[action].SetStatus(status);
        }
    }

    public void ShowStatus(GlobalShortcutAction action, string status) => _rows[action].SetStatus(status);

    private void BeginCapture(GlobalShortcutAction action)
    {
        CancelCapture();
        _capturing = action;
        _rows[action].SetKeysText("Press shortcut…");
        _rows[action].SetStatus(ShortcutCapture.Prompt);
        Keyboard.Focus(this);
    }

    private void CancelCapture()
    {
        if (_capturing is not { } action)
        {
            return;
        }

        _capturing = null;
        _rows[action].SetKeysText(_keysText.GetValueOrDefault(action, string.Empty));
        _rows[action].SetStatus(string.Empty);
    }

    private void TimerSettingsPage_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing is not { } action)
        {
            return;
        }

        e.Handled = true;
        switch (ShortcutCapture.Read(e, out var chord))
        {
            case ShortcutKeyResult.Cancelled:
                CancelCapture();
                break;
            case ShortcutKeyResult.Invalid:
                _rows[action].SetStatus(ShortcutCapture.InvalidKeysMessage);
                break;
            case ShortcutKeyResult.Captured:
                Capture(action, chord!);
                break;
        }
    }

    private void TimerSettingsPage_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_capturing is { } action && ShortcutCapture.TryReadMouseButton(e, out var chord))
        {
            e.Handled = true;
            Capture(action, chord!);
        }
    }

    private void TimerSettingsPage_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_capturing is { } action && ShortcutCapture.TryReadWheel(e, out var chord))
        {
            e.Handled = true;
            Capture(action, chord!);
        }
    }

    private void Capture(GlobalShortcutAction action, GlobalHotkeyChord chord)
    {
        _capturing = null;
        _rows[action].SetKeysText(ShortcutText.Format(chord));
        _rows[action].SetStatus(string.Empty);
        ChangeRequested?.Invoke(action, chord);
    }

    // Clicking away, or leaving the page, ends a capture.
    private void TimerSettingsPage_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!IsKeyboardFocusWithin)
        {
            CancelCapture();
        }
    }
}
