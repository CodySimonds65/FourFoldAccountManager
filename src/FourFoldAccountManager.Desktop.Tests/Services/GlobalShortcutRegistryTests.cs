using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class GlobalShortcutRegistryTests
{
    private static readonly GlobalHotkeyChord NumPad1 = new(0x61, GlobalHotkeyModifiers.None);

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
