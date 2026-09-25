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
    public async Task SettingsWithoutTimerShortcutsLoadTheDefaults()
    {
        await WriteSettingsAsync(json =>
        {
            json.Remove("timerSplitShortcut");
            json.Remove("timerFinishShortcut");
            json.Remove("timerResetShortcut");
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(GlobalHotkeyChord.DefaultTimerSplit, loaded.TimerSplitShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerFinish, loaded.TimerFinishShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerReset, loaded.TimerResetShortcut);
    }

    [Fact]
    public async Task InvalidTimerShortcutsFallBackToDefaultsWithoutFailingTheLoad()
    {
        await WriteSettingsAsync(json =>
        {
            json["timerSplitShortcut"] = new JsonObject { ["virtualKey"] = 0x41, ["modifiers"] = 0 };
            json["timerFinishShortcut"] = null;
            json["timerResetShortcut"] = new JsonObject { ["virtualKey"] = 0x61, ["modifiers"] = 16 };
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(GlobalHotkeyChord.DefaultTimerSplit, loaded.TimerSplitShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerFinish, loaded.TimerFinishShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerReset, loaded.TimerResetShortcut);
    }

    [Fact]
    public async Task MalformedTimerShortcutShapesFallBackToDefaultsWithoutFailingTheLoad()
    {
        await WriteSettingsAsync(json =>
        {
            // Whole value has the wrong JSON kind (string instead of an object).
            json["timerSplitShortcut"] = "Numpad1";
            // virtualKey is outside ushort range.
            json["timerFinishShortcut"] = new JsonObject { ["virtualKey"] = 70000 };
            // modifiers has the wrong JSON kind (string instead of a number).
            json["timerResetShortcut"] = new JsonObject { ["virtualKey"] = 0x52, ["modifiers"] = "Ctrl" };
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(GlobalHotkeyChord.DefaultTimerSplit, loaded.TimerSplitShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerFinish, loaded.TimerFinishShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerReset, loaded.TimerResetShortcut);
    }

    [Fact]
    public async Task SavedTimerShortcutJsonShapeMatchesAnUnconvertedShortcut()
    {
        var split = new GlobalHotkeyChord(0x61, GlobalHotkeyModifiers.None);
        await _store.SaveAsync(PanelSettings.Default with { TimerSplitShortcut = split });

        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        var timerNode = json["timerSplitShortcut"]!.AsObject();
        var revealNode = json["revealXpOverlayTabShortcut"]!.AsObject();

        Assert.Equal(
            revealNode.Select(property => property.Key).OrderBy(key => key, StringComparer.Ordinal),
            timerNode.Select(property => property.Key).OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal((int)split.VirtualKey, (int)timerNode["virtualKey"]!);
        Assert.Equal((int)split.Modifiers, (int)timerNode["modifiers"]!);
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

    private async Task WriteSettingsAsync(Action<JsonObject> edit)
    {
        await _store.SaveAsync(PanelSettings.Default);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        edit(json);
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());
    }
}
