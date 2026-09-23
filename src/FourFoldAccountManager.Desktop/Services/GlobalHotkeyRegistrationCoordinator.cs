using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Services;

internal interface IGlobalHotkeyRegistrar
{
    bool TryRegister(int id, GlobalHotkeyChord chord);

    void Unregister(int id);
}

internal sealed record GlobalHotkeyShortcutChange(
    GlobalHotkeyChord Previous,
    GlobalHotkeyChord Current);

internal sealed class GlobalHotkeyRegistrationCoordinator : IDisposable
{
    private const int MaximumApplicationHotkeyId = 0xBFFF;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _replacementGate = new(1, 1);
    private readonly IGlobalHotkeyRegistrar _registrar;
    private readonly Dictionary<int, GlobalHotkeyChord> _activeRegistrations = [];
    private readonly Dictionary<int, GlobalHotkeyChord> _pendingRegistrations = [];
    private readonly HashSet<int> _registeredIds = [];
    private int _nextId;
    private bool _disposed;

    public GlobalHotkeyRegistrationCoordinator(IGlobalHotkeyRegistrar registrar)
    {
        _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
    }

    public bool TryInitialize(GlobalHotkeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (!chord.IsValid)
        {
            return false;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            if (_activeRegistrations.Values.Contains(chord))
            {
                return true;
            }

            if (_pendingRegistrations.Values.Contains(chord))
            {
                return false;
            }

            var id = NextId();
            if (!_registrar.TryRegister(id, chord))
            {
                return false;
            }

            _registeredIds.Add(id);
            _activeRegistrations.Add(id, chord);
            return true;
        }
    }

    public bool IsCurrent(int id)
    {
        lock (_sync)
        {
            return !_disposed && _activeRegistrations.ContainsKey(id);
        }
    }

    public bool TryGetChord(int id, out GlobalHotkeyChord chord)
    {
        lock (_sync)
        {
            if (!_disposed && _activeRegistrations.TryGetValue(id, out chord!))
            {
                return true;
            }

            chord = null!;
            return false;
        }
    }

    public Task<bool> TryReplaceAsync(GlobalHotkeyChord chord, Func<Task> persist)
    {
        ArgumentNullException.ThrowIfNull(chord);
        GlobalHotkeyChord previous;
        lock (_sync)
        {
            if (_disposed)
            {
                return Task.FromResult(false);
            }

            previous = _activeRegistrations.Values.FirstOrDefault() ?? chord;
        }

        return TryReplaceAsync([new GlobalHotkeyShortcutChange(previous, chord)], persist);
    }

    public Task<bool> TryReplaceAsync(
        GlobalHotkeyChord previous,
        GlobalHotkeyChord current,
        Func<Task> persist)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        return TryReplaceAsync([new GlobalHotkeyShortcutChange(previous, current)], persist);
    }

    public async Task<bool> TryReplaceAsync(
        IReadOnlyCollection<GlobalHotkeyShortcutChange> changes,
        Func<Task> persist)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(persist);
        var replacements = changes.ToArray();
        if (replacements.Any(change =>
                change.Previous is null || change.Current is null || !change.Current.IsValid) ||
            replacements.Select(change => change.Previous).Distinct().Count() != replacements.Length ||
            replacements.Select(change => change.Current).Distinct().Count() != replacements.Length)
        {
            return false;
        }

        await _replacementGate.WaitAsync();
        try
        {
            var changed = replacements
                .Where(change => change.Previous != change.Current)
                .ToArray();
            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                var replacedChords = changed.Select(change => change.Previous).ToHashSet();
                var currentUnchangedChords = _activeRegistrations.Values
                    .Where(chord => !replacedChords.Contains(chord))
                    .Concat(_pendingRegistrations.Values)
                    .ToHashSet();
                if (changed.Any(change => currentUnchangedChords.Contains(change.Current)))
                {
                    return false;
                }
            }

            if (changed.Length == 0)
            {
                await persist();
                return true;
            }

            var candidates = new Dictionary<GlobalHotkeyShortcutChange, int>();
            var releasedRegistrations = new Dictionary<int, GlobalHotkeyChord>();
            var registrationSucceeded = true;
            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                var replacementChords = changed.Select(change => change.Current).ToHashSet();
                foreach (var (id, activeChord) in _activeRegistrations)
                {
                    if (replacementChords.Contains(activeChord))
                    {
                        _registrar.Unregister(id);
                        releasedRegistrations.Add(id, activeChord);
                    }
                }

                foreach (var change in changed)
                {
                    var id = NextId();
                    if (!_registrar.TryRegister(id, change.Current))
                    {
                        foreach (var candidateId in candidates.Values)
                        {
                            ReleasePending(candidateId);
                        }

                        registrationSucceeded = false;
                        break;
                    }

                    _registeredIds.Add(id);
                    _pendingRegistrations.Add(id, change.Current);
                    candidates.Add(change, id);
                }
            }

            if (!registrationSucceeded)
            {
                RestoreReleasedRegistrations(releasedRegistrations);
                return false;
            }

            try
            {
                await persist();
            }
            catch
            {
                foreach (var candidateId in candidates.Values)
                {
                    ReleasePending(candidateId);
                }

                RestoreReleasedRegistrations(releasedRegistrations);
                throw;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                if (candidates.Values.Any(candidateId =>
                        !_pendingRegistrations.ContainsKey(candidateId) ||
                        !_registeredIds.Contains(candidateId)))
                {
                    foreach (var candidateId in candidates.Values)
                    {
                        ReleasePending(candidateId);
                    }

                    RestoreReleasedRegistrations(releasedRegistrations);
                    return false;
                }

                var previousIds = changed.ToDictionary(
                    change => change,
                    change => FindActiveId(change.Previous));
                foreach (var change in changed)
                {
                    var oldId = previousIds[change];
                    if (oldId is int previousId && _activeRegistrations.Remove(previousId))
                    {
                        _registeredIds.Remove(previousId);
                        if (!releasedRegistrations.ContainsKey(previousId))
                        {
                            _registrar.Unregister(previousId);
                        }
                    }

                    var candidateId = candidates[change];
                    _pendingRegistrations.Remove(candidateId);
                    _activeRegistrations.Add(candidateId, change.Current);
                }

                return true;
            }
        }
        finally
        {
            _replacementGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var id in _registeredIds)
            {
                _registrar.Unregister(id);
            }

            _registeredIds.Clear();
            _activeRegistrations.Clear();
            _pendingRegistrations.Clear();
        }
    }

    private int? FindActiveId(GlobalHotkeyChord chord)
    {
        foreach (var (id, activeChord) in _activeRegistrations)
        {
            if (activeChord == chord)
            {
                return id;
            }
        }

        return null;
    }

    private int NextId()
    {
        for (var attempt = 0; attempt < MaximumApplicationHotkeyId; attempt++)
        {
            _nextId = _nextId >= MaximumApplicationHotkeyId ? 1 : _nextId + 1;
            if (!_registeredIds.Contains(_nextId))
            {
                return _nextId;
            }
        }

        throw new InvalidOperationException("No available application hotkey identifiers remain.");
    }

    private void ReleasePending(int id)
    {
        lock (_sync)
        {
            _pendingRegistrations.Remove(id);
            if (_registeredIds.Remove(id))
            {
                _registrar.Unregister(id);
            }
        }
    }

    private bool RestoreReleasedRegistrations(
        IReadOnlyDictionary<int, GlobalHotkeyChord> releasedRegistrations)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            var restored = true;
            foreach (var (id, chord) in releasedRegistrations)
            {
                if (_registrar.TryRegister(id, chord))
                {
                    continue;
                }

                _activeRegistrations.Remove(id);
                _registeredIds.Remove(id);
                restored = false;
            }

            return restored;
        }
    }
}
