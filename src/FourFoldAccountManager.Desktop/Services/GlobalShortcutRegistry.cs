using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Services;

// Registers every app shortcut action with Windows, maps WM_HOTKEY ids back to actions, and tracks which
// actions are live. The coordinator it wraps identifies registrations by chord, so this class decides which
// action owns a chord.
internal sealed class GlobalShortcutRegistry : IDisposable
{
    private readonly GlobalHotkeyRegistrationCoordinator _coordinator;
    private readonly HashSet<GlobalShortcutAction> _available = [];

    public GlobalShortcutRegistry(IGlobalHotkeyRegistrar registrar)
    {
        _coordinator = new GlobalHotkeyRegistrationCoordinator(registrar);
    }

    public bool IsAvailable(GlobalShortcutAction action) => _available.Contains(action);

    public void Initialize(PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _available.Clear();
        RegisterUnavailable(settings);
    }

    public bool TryResolve(int hotkeyId, PanelSettings settings, out GlobalShortcutAction action)
    {
        ArgumentNullException.ThrowIfNull(settings);
        action = default;
        if (!_coordinator.TryGetChord(hotkeyId, out var chord))
        {
            return false;
        }

        foreach (var candidate in GlobalShortcutActions.All)
        {
            if (_available.Contains(candidate) && GlobalShortcutActions.GetChord(settings, candidate) == chord)
            {
                action = candidate;
                return true;
            }
        }

        return false;
    }

    // Replaces live registrations atomically around persist; returns false when Windows refuses a new chord,
    // in which case nothing was persisted and the previous registrations remain.
    public async Task<bool> ApplyAsync(PanelSettings current, PanelSettings next, Func<Task> persist)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(persist);
        var replacements = GlobalShortcutActions.All
            .Where(action => _available.Contains(action) &&
                GlobalShortcutActions.GetChord(current, action) != GlobalShortcutActions.GetChord(next, action))
            .Select(action => new GlobalHotkeyShortcutChange(
                GlobalShortcutActions.GetChord(current, action),
                GlobalShortcutActions.GetChord(next, action)))
            .ToArray();
        if (!await _coordinator.TryReplaceAsync(replacements, persist))
        {
            return false;
        }

        RegisterUnavailable(next);
        return true;
    }

    public void Dispose() => _coordinator.Dispose();

    // Actions without a registration (refused by Windows, or sharing keys with an earlier action) are tried
    // whenever their keys are not already owned by an available action.
    private void RegisterUnavailable(PanelSettings settings)
    {
        foreach (var action in GlobalShortcutActions.All.Where(action => !_available.Contains(action)).ToArray())
        {
            var chord = GlobalShortcutActions.GetChord(settings, action);
            var claimed = GlobalShortcutActions.All.Any(other =>
                _available.Contains(other) && GlobalShortcutActions.GetChord(settings, other) == chord);
            if (!claimed && _coordinator.TryInitialize(chord))
            {
                _available.Add(action);
            }
        }
    }
}
