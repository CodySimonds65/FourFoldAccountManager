using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class GlobalShortcutRegistryTests
{
    private static readonly GlobalHotkeyChord NumPad1 = new(0x61, GlobalHotkeyModifiers.None);
    private static readonly GlobalHotkeyChord PlainF = new(0x46, GlobalHotkeyModifiers.None);
    private static readonly GlobalHotkeyChord PlainG = new(0x47, GlobalHotkeyModifiers.None);
    private static readonly GlobalHotkeyChord PlainH = new(0x48, GlobalHotkeyModifiers.None);

    [Fact]
    public void InitializeRegistersEveryDefaultShortcutAndResolvesEachToItsAction()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var settings = PanelSettings.Default;

        registry.Initialize(settings);

        Assert.Equal(5, registrar.Registered.Count);
        foreach (var action in GlobalShortcutActions.All)
        {
            Assert.True(registry.IsAvailable(action));
            Assert.True(registry.TryResolve(
                registrar.IdOf(GlobalShortcutActions.GetChord(settings, action)), settings, out var resolved));
            Assert.Equal(action, resolved);
        }
    }

    [Fact]
    public async Task ApplyKeepsTheOldShortcutAndSavesNothingWhenWindowsRefusesTheNewOne()
    {
        var registrar = new FakeRegistrar();
        registrar.Refused.Add(NumPad1);
        using var registry = new GlobalShortcutRegistry(registrar);
        var current = PanelSettings.Default;
        registry.Initialize(current);
        var next = GlobalShortcutActions.WithChord(current, GlobalShortcutAction.TimerSplit, NumPad1);
        var persisted = 0;

        var applied = await registry.ApplyAsync(current, next, () => { persisted++; return Task.CompletedTask; });

        Assert.False(applied.Saved);
        Assert.Equal(0, persisted);
        Assert.True(registry.IsAvailable(GlobalShortcutAction.TimerSplit));
        Assert.True(registry.TryResolve(registrar.IdOf(GlobalHotkeyChord.DefaultTimerSplit), current, out var resolved));
        Assert.Equal(GlobalShortcutAction.TimerSplit, resolved);
    }

    [Fact]
    public async Task FailedSaveMixingPlainKeysAndChordsKeepsEveryOldBindingAndNeverGivesPlainKeysToWindows()
    {
        var windows = new FakeRegistrar();
        var refusedChord = new GlobalHotkeyChord(0x61, GlobalHotkeyModifiers.Control);
        windows.Refused.Add(refusedChord);
        using var registry = new GlobalShortcutRegistry(new PlainKeyRoutingRegistrar(windows, plainKeysAvailable: true));
        var current = GlobalShortcutActions.WithChord(PanelSettings.Default, GlobalShortcutAction.TimerFinish, PlainG);
        registry.Initialize(current);
        var next = GlobalShortcutActions.WithChord(current, GlobalShortcutAction.TimerSplit, PlainF);
        next = GlobalShortcutActions.WithChord(next, GlobalShortcutAction.TimerFinish, PlainH);
        next = GlobalShortcutActions.WithChord(next, GlobalShortcutAction.TimerReset, refusedChord);
        var persisted = 0;

        var applied = await registry.ApplyAsync(current, next, () => { persisted++; return Task.CompletedTask; });

        Assert.False(applied.Saved);
        Assert.Equal(0, persisted);
        Assert.Equal(
            GlobalShortcutActions.All.ToDictionary(action => action, action => GlobalShortcutActions.GetChord(current, action)),
            registry.ActiveChords(current));
        Assert.True(registry.TryResolve(windows.IdOf(GlobalHotkeyChord.DefaultTimerSplit), current, out var resolved));
        Assert.Equal(GlobalShortcutAction.TimerSplit, resolved);
        // RegisterHotKey would take a plain key from every other app, so plain keys must never reach Windows.
        Assert.DoesNotContain(windows.Registered.Values, chord => chord.IsPlainKey);

        // Retrying without the refused chord must succeed: a plain key leaked by the rollback would be refused
        // as a duplicate here, leaving the user unable to bind it.
        var retry = GlobalShortcutActions.WithChord(next, GlobalShortcutAction.TimerReset, GlobalHotkeyChord.DefaultTimerReset);
        var retried = await registry.ApplyAsync(current, retry, () => { persisted++; return Task.CompletedTask; });

        Assert.True(retried.Saved);
        Assert.Empty(retried.StillUnavailable);
        Assert.Equal(1, persisted);
        Assert.Equal(PlainF, registry.ActiveChords(retry)[GlobalShortcutAction.TimerSplit]);
        Assert.Equal(PlainH, registry.ActiveChords(retry)[GlobalShortcutAction.TimerFinish]);
        Assert.DoesNotContain(windows.Registered.Values, chord => chord.IsPlainKey);
    }

    private sealed class FakeRegistrar : IGlobalHotkeyRegistrar
    {
        public HashSet<GlobalHotkeyChord> Refused { get; } = [];

        public Dictionary<int, GlobalHotkeyChord> Registered { get; } = [];

        public bool TryRegister(int id, GlobalHotkeyChord chord)
        {
            if (Refused.Contains(chord) || Registered.ContainsValue(chord))
            {
                return false;
            }

            Registered[id] = chord;
            return true;
        }

        public void Unregister(int id) => Registered.Remove(id);

        public int IdOf(GlobalHotkeyChord chord) => Registered.Single(entry => entry.Value == chord).Key;
    }
}
