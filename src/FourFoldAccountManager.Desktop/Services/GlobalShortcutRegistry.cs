using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Services;

// Result of ApplyAsync: Saved is false only when Windows refused a new chord and nothing was persisted.
// When Saved is true, StillUnavailable lists changed actions that remain unregistered after saving (the
// shared-keys case: their old chord was owned by another, still-active action, so the change could not go
// through the atomic replace and the fresh registration attempted after saving failed).
internal sealed record ShortcutApplyResult(bool Saved, IReadOnlyList<GlobalShortcutAction> StillUnavailable)
{
    public static ShortcutApplyResult NotSaved { get; } = new(false, Array.Empty<GlobalShortcutAction>());
}

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

    // Replaces live registrations atomically around persist. Returns Saved = false when Windows refuses a
    // new chord for an atomically-replaced action, in which case nothing was persisted and the previous
    // registrations remain. An unavailable action whose old chord is currently owned by another, active
    // action (the shared-keys case) cannot go through the atomic replace — its old chord isn't really "its"
    // active registration, so replacing it there would risk unregistering the action that owns it. That
    // action is instead (re)tried after a successful save; if it is still unavailable afterward, it is
    // reported back via StillUnavailable so the caller can warn about it instead of claiming success.
    public async Task<ShortcutApplyResult> ApplyAsync(PanelSettings current, PanelSettings next, Func<Task> persist)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(persist);
        var changed = GlobalShortcutActions.All
            .Where(action => GlobalShortcutActions.GetChord(current, action) != GlobalShortcutActions.GetChord(next, action))
            .ToArray();
        var atomic = changed
            .Where(action => _available.Contains(action) || !IsChordOwnedByAnAvailableAction(current, action))
            .ToArray();
        var replacements = atomic
            .Select(action => new GlobalHotkeyShortcutChange(
                GlobalShortcutActions.GetChord(current, action),
                GlobalShortcutActions.GetChord(next, action)))
            .ToArray();
        if (!await _coordinator.TryReplaceAsync(replacements, persist))
        {
            return ShortcutApplyResult.NotSaved;
        }

        foreach (var action in atomic)
        {
            _available.Add(action);
        }

        RegisterUnavailable(next);
        var stillUnavailable = changed.Where(action => !_available.Contains(action)).ToArray();
        return new ShortcutApplyResult(true, stillUnavailable);
    }

    private bool IsChordOwnedByAnAvailableAction(PanelSettings settings, GlobalShortcutAction action)
    {
        var chord = GlobalShortcutActions.GetChord(settings, action);
        return GlobalShortcutActions.All.Any(other =>
            _available.Contains(other) && GlobalShortcutActions.GetChord(settings, other) == chord);
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
