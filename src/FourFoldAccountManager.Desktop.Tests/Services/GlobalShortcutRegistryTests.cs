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
    public void LaterActionSharingKeysWithAnEarlierOneStaysUnregistered()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var settings = PanelSettings.Default with
        {
            TimerSplitShortcut = PanelSettings.Default.RevealXpOverlayTabShortcut
        };

        registry.Initialize(settings);

        Assert.Equal(4, registrar.Registered.Count);
        Assert.True(registry.IsAvailable(GlobalShortcutAction.RevealOverlays));
        Assert.False(registry.IsAvailable(GlobalShortcutAction.TimerSplit));
        Assert.True(registry.TryResolve(
            registrar.IdOf(settings.RevealXpOverlayTabShortcut), settings, out var resolved));
        Assert.Equal(GlobalShortcutAction.RevealOverlays, resolved);
    }

    [Fact]
    public void ShortcutWindowsRefusesIsUnavailable()
    {
        var registrar = new FakeRegistrar();
        registrar.Refused.Add(GlobalHotkeyChord.DefaultTimerReset);
        using var registry = new GlobalShortcutRegistry(registrar);

        registry.Initialize(PanelSettings.Default);

        Assert.False(registry.IsAvailable(GlobalShortcutAction.TimerReset));
        Assert.True(registry.IsAvailable(GlobalShortcutAction.TimerSplit));
    }

    [Fact]
    public void UnknownHotkeyIdDoesNotResolve()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        registry.Initialize(PanelSettings.Default);

        Assert.False(registry.TryResolve(9_999, PanelSettings.Default, out _));
    }

    [Fact]
    public async Task ApplyReplacesAChangedShortcutAfterSaving()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var current = PanelSettings.Default;
        registry.Initialize(current);
        var next = GlobalShortcutActions.WithChord(current, GlobalShortcutAction.TimerSplit, NumPad1);
        var persisted = 0;

        var applied = await registry.ApplyAsync(current, next, () => { persisted++; return Task.CompletedTask; });

        Assert.True(applied);
        Assert.Equal(1, persisted);
        Assert.DoesNotContain(GlobalHotkeyChord.DefaultTimerSplit, registrar.Registered.Values);
        Assert.True(registry.TryResolve(registrar.IdOf(NumPad1), next, out var resolved));
        Assert.Equal(GlobalShortcutAction.TimerSplit, resolved);
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

        Assert.False(applied);
        Assert.Equal(0, persisted);
        Assert.True(registry.IsAvailable(GlobalShortcutAction.TimerSplit));
        Assert.True(registry.TryResolve(registrar.IdOf(GlobalHotkeyChord.DefaultTimerSplit), current, out var resolved));
        Assert.Equal(GlobalShortcutAction.TimerSplit, resolved);
    }

    [Fact]
    public async Task ChangingAnActionThatSharedKeysLeavesTheEarlierActionRegistered()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var current = PanelSettings.Default with
        {
            TimerSplitShortcut = PanelSettings.Default.RevealXpOverlayTabShortcut
        };
        registry.Initialize(current);
        var next = GlobalShortcutActions.WithChord(current, GlobalShortcutAction.TimerSplit, NumPad1);

        Assert.True(await registry.ApplyAsync(current, next, () => Task.CompletedTask));

        Assert.True(registry.IsAvailable(GlobalShortcutAction.RevealOverlays));
        Assert.True(registry.IsAvailable(GlobalShortcutAction.TimerSplit));
        Assert.True(registry.TryResolve(registrar.IdOf(next.RevealXpOverlayTabShortcut), next, out var reveal));
        Assert.Equal(GlobalShortcutAction.RevealOverlays, reveal);
        Assert.True(registry.TryResolve(registrar.IdOf(NumPad1), next, out var split));
        Assert.Equal(GlobalShortcutAction.TimerSplit, split);
    }

    [Fact]
    public async Task ApplyWithoutShortcutChangesStillSaves()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var current = PanelSettings.Default;
        registry.Initialize(current);
        var persisted = 0;

        Assert.True(await registry.ApplyAsync(current, current with { FillGameToPanel = true },
            () => { persisted++; return Task.CompletedTask; }));

        Assert.Equal(1, persisted);
        Assert.Equal(5, registrar.Registered.Count);
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
