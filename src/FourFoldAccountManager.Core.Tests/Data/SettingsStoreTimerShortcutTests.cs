using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStoreTimerShortcutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-timer-settings-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStoreTimerShortcutTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task MalformedShortcutShapesFallBackToDefaultsWithoutFailingTheLoad()
    {
        await WriteSettingsAsync(json =>
        {
            // Whole value has the wrong JSON kind (string instead of an object).
            json["timerSplitShortcut"] = "Numpad1";
            json["revealXpOverlayTabShortcut"] = "Shift+O";
            // virtualKey is outside ushort range.
            json["timerFinishShortcut"] = new JsonObject { ["virtualKey"] = 70000 };
            // modifiers has the wrong JSON kind (string instead of a number).
            json["timerResetShortcut"] = new JsonObject { ["virtualKey"] = 0x52, ["modifiers"] = "Ctrl" };
            json["toggleDividerResizingShortcut"] = new JsonObject { ["virtualKey"] = 0x4C, ["modifiers"] = "Ctrl" };
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(GlobalHotkeyChord.DefaultRevealXpOverlayTab, loaded.RevealXpOverlayTabShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultToggleDividerResizing, loaded.ToggleDividerResizingShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerSplit, loaded.TimerSplitShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerFinish, loaded.TimerFinishShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerReset, loaded.TimerResetShortcut);
    }

    [Fact]
    public async Task CustomTimerShortcutsRoundTrip()
    {
        var split = new GlobalHotkeyChord(0x61, GlobalHotkeyModifiers.None);
        var finish = new GlobalHotkeyChord(0x7C, GlobalHotkeyModifiers.None);
        var reset = new GlobalHotkeyChord(0x52, GlobalHotkeyModifiers.Control);

        await _store.SaveAsync(PanelSettings.Default with
        {
            TimerSplitShortcut = split,
            TimerFinishShortcut = finish,
            TimerResetShortcut = reset
        });
        var loaded = await _store.LoadAsync();

        Assert.Equal(split, loaded.TimerSplitShortcut);
        Assert.Equal(finish, loaded.TimerFinishShortcut);
        Assert.Equal(reset, loaded.TimerResetShortcut);
    }

    [Fact]
    public async Task PlainKeyShortcutsSurviveARestart()
    {
        // Before plain keys were allowed, an invalid reveal or divider shortcut failed validation outright.
        var reveal = new GlobalHotkeyChord(0x4F, GlobalHotkeyModifiers.None);
        var divider = new GlobalHotkeyChord(0x75, GlobalHotkeyModifiers.None);
        var split = new GlobalHotkeyChord(0x46, GlobalHotkeyModifiers.None);

        await _store.SaveAsync(PanelSettings.Default with
        {
            RevealXpOverlayTabShortcut = reveal,
            ToggleDividerResizingShortcut = divider,
            TimerSplitShortcut = split
        });
        var loaded = await _store.LoadAsync();

        Assert.Equal(reveal, loaded.RevealXpOverlayTabShortcut);
        Assert.Equal(divider, loaded.ToggleDividerResizingShortcut);
        Assert.Equal(split, loaded.TimerSplitShortcut);
    }

    [Fact]
    public async Task F5ShortcutsFallBackToDefaultsWithoutFailingTheLoad()
    {
        // F5 stays a browser refresh for the game panels, so F5 shortcuts saved by earlier builds are dropped.
        await WriteSettingsAsync(json =>
        {
            json["revealXpOverlayTabShortcut"] = new JsonObject { ["virtualKey"] = 0x74, ["modifiers"] = 1 };
            json["timerSplitShortcut"] = new JsonObject { ["virtualKey"] = 0x74, ["modifiers"] = 0 };
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(GlobalHotkeyChord.DefaultRevealXpOverlayTab, loaded.RevealXpOverlayTabShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerSplit, loaded.TimerSplitShortcut);
    }

    private async Task WriteSettingsAsync(Action<JsonObject> edit)
    {
        await _store.SaveAsync(PanelSettings.Default);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        edit(json);
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());
    }
}
