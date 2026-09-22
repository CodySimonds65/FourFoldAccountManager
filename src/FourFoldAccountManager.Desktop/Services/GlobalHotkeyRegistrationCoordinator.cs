using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Services;

internal interface IGlobalHotkeyRegistrar
{
    bool TryRegister(int id, GlobalHotkeyChord chord);

    void Unregister(int id);
}

internal sealed class GlobalHotkeyRegistrationCoordinator : IDisposable
{
    private const int MaximumApplicationHotkeyId = 0xBFFF;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _replacementGate = new(1, 1);
    private readonly IGlobalHotkeyRegistrar _registrar;
    private readonly HashSet<int> _registeredIds = [];
    private readonly HashSet<int> _pendingIds = [];
    private int? _activeId;
    private GlobalHotkeyChord? _activeChord;
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

            if (_activeId is not null)
            {
                return _activeChord == chord;
            }

            var id = NextId();
            if (!_registrar.TryRegister(id, chord))
            {
                return false;
            }

            _registeredIds.Add(id);
            _activeId = id;
            _activeChord = chord;
            return true;
        }
    }

    public bool IsCurrent(int id)
    {
        lock (_sync)
        {
            return !_disposed && _activeId == id;
        }
    }

    public async Task<bool> TryReplaceAsync(GlobalHotkeyChord chord, Func<Task> persist)
    {
        ArgumentNullException.ThrowIfNull(chord);
        ArgumentNullException.ThrowIfNull(persist);
        if (!chord.IsValid)
        {
            return false;
        }

        await _replacementGate.WaitAsync();
        try
        {
            int? candidateId = null;
            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                if (_activeId is null || _activeChord != chord)
                {
                    var id = NextId();
                    if (!_registrar.TryRegister(id, chord))
                    {
                        return false;
                    }

                    _registeredIds.Add(id);
                    _pendingIds.Add(id);
                    candidateId = id;
                }
            }

            try
            {
                await persist();
            }
            catch
            {
                if (candidateId is int failedId)
                {
                    ReleasePending(failedId);
                }

                throw;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                if (candidateId is not int committedId)
                {
                    return _activeId is not null && _activeChord == chord;
                }

                if (!_pendingIds.Remove(committedId) || !_registeredIds.Contains(committedId))
                {
                    return false;
                }

                var previousId = _activeId;
                _activeId = committedId;
                _activeChord = chord;

                if (previousId is int oldId && _registeredIds.Remove(oldId))
                {
                    _registrar.Unregister(oldId);
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
            _pendingIds.Clear();
            _activeId = null;
            _activeChord = null;
        }
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
            _pendingIds.Remove(id);
            if (_registeredIds.Remove(id))
            {
                _registrar.Unregister(id);
            }
        }
    }
}
