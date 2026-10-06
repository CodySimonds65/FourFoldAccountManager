using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Services;

// RegisterHotKey takes its key from every other app and cannot take a mouse button at all, so plain keys and
// mouse inputs never go there. They "register" by being listened for through raw input: available exactly
// when the raw input listener is, and refused on the same input twice, like Windows refuses a duplicate
// chord. Key chords with a modifier pass through unchanged, so the coordinator's atomic replace covers both
// kinds.
internal sealed class PlainKeyRoutingRegistrar(IGlobalHotkeyRegistrar chords, bool plainKeysAvailable)
    : IGlobalHotkeyRegistrar
{
    private readonly Dictionary<int, GlobalHotkeyChord> _plainKeys = [];

    public bool TryRegister(int id, GlobalHotkeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (!chord.IsObserved)
        {
            return chords.TryRegister(id, chord);
        }

        if (!plainKeysAvailable || !chord.IsValid || _plainKeys.ContainsValue(chord))
        {
            return false;
        }

        _plainKeys[id] = chord;
        return true;
    }

    public void Unregister(int id)
    {
        if (!_plainKeys.Remove(id))
        {
            chords.Unregister(id);
        }
    }
}
