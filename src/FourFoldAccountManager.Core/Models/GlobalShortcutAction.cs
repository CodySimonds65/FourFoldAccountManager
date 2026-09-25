namespace FourFoldAccountManager.Core.Models;

// Order matters: when a hand-edited settings file gives two actions the same keys, the earlier action keeps them.
public enum GlobalShortcutAction
{
    RevealOverlays,
    ToggleDividerResizing,
    TimerSplit,
    TimerFinish,
    TimerReset
}

public static class GlobalShortcutActions
{
    public static IReadOnlyList<GlobalShortcutAction> All { get; } =
        Array.AsReadOnly(Enum.GetValues<GlobalShortcutAction>());

    public static GlobalHotkeyChord GetChord(PanelSettings settings, GlobalShortcutAction action)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return action switch
        {
            GlobalShortcutAction.RevealOverlays => settings.RevealXpOverlayTabShortcut,
            GlobalShortcutAction.ToggleDividerResizing => settings.ToggleDividerResizingShortcut,
            GlobalShortcutAction.TimerSplit => settings.TimerSplitShortcut,
            GlobalShortcutAction.TimerFinish => settings.TimerFinishShortcut,
            GlobalShortcutAction.TimerReset => settings.TimerResetShortcut,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown shortcut action.")
        };
    }

    public static PanelSettings WithChord(PanelSettings settings, GlobalShortcutAction action, GlobalHotkeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(chord);
        return action switch
        {
            GlobalShortcutAction.RevealOverlays => settings with { RevealXpOverlayTabShortcut = chord },
            GlobalShortcutAction.ToggleDividerResizing => settings with { ToggleDividerResizingShortcut = chord },
            GlobalShortcutAction.TimerSplit => settings with { TimerSplitShortcut = chord },
            GlobalShortcutAction.TimerFinish => settings with { TimerFinishShortcut = chord },
            GlobalShortcutAction.TimerReset => settings with { TimerResetShortcut = chord },
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown shortcut action.")
        };
    }

    public static string DisplayName(GlobalShortcutAction action) => action switch
    {
        GlobalShortcutAction.RevealOverlays => "Reveal overlays tab",
        GlobalShortcutAction.ToggleDividerResizing => "Toggle divider resizing",
        GlobalShortcutAction.TimerSplit => "Timer split",
        GlobalShortcutAction.TimerFinish => "Timer finish",
        GlobalShortcutAction.TimerReset => "Timer reset",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown shortcut action.")
    };

    // Returns the first pair of actions that share keys, earlier action first, or null when all differ.
    public static (GlobalShortcutAction First, GlobalShortcutAction Second)? FindDuplicate(
        IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        var seen = new Dictionary<GlobalHotkeyChord, GlobalShortcutAction>();
        foreach (var action in All)
        {
            if (!chords.TryGetValue(action, out var chord))
            {
                continue;
            }

            if (seen.TryGetValue(chord, out var earlier))
            {
                return (earlier, action);
            }

            seen.Add(chord, action);
        }

        return null;
    }
}
