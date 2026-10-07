using System.Text.Json.Serialization;
using FourFoldAccountManager.Core.Plugins;

namespace FourFoldAccountManager.Core.Models;

public sealed record PanelSettings
{
    [JsonConstructor]
    public PanelSettings(PanelLayout layout, IReadOnlyList<Guid?> slotAccountIds)
    {
        ArgumentNullException.ThrowIfNull(slotAccountIds);
        Layout = layout;
        SlotAccountIds = Array.AsReadOnly(slotAccountIds.ToArray());
    }

    public PanelLayout Layout { get; init; }

    public IReadOnlyList<Guid?> SlotAccountIds { get; init; }

    public bool FillGameToPanel { get; init; } = false;

    public bool ShareLinkedAccounts { get; init; } = false;

    public bool ShowFullScreenExitButton { get; init; } = true;

    // Whether the plugin panel is open beside the strip. The name and JSON key predate the strip, so existing settings
    // files keep their shown-or-hidden choice.
    public bool PluginsSidebarExpanded { get; init; } = true;

    // Whether the whole plugin strip and its panel show in the main window; the toolbar button toggles it.
    public bool PluginStripVisible { get; init; } = true;

    // Loads community plugins from the dev plugins folder, for plugin authors.
    public bool PluginDeveloperMode { get; init; }

    // Plugin ids in strip order. Empty means the built-in order. Unknown ids are kept for plugins that may return.
    [JsonConverter(typeof(LenientListJsonConverter<string>))]
    public IReadOnlyList<string> PluginOrder { get; init; } = Array.Empty<string>();

    [JsonConverter(typeof(LenientListJsonConverter<string>))]
    public IReadOnlyList<string> DisabledPlugins { get; init; } = Array.Empty<string>();

    // The plugin last opened; the panel shows it when PluginsSidebarExpanded is true.
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? OpenPlugin { get; init; } = BuiltInPlugins.XpTrackerId;

    public bool ShowOverlaysInTheatreMode { get; init; } = true;

    public bool BlockStorePages { get; init; }

    // Reads the game's own traffic in each panel (read-only) and sends live battle and scene events to plugins. On by
    // default; a settings file from before the feed has no value and gets this default.
    public bool LiveGameFeed { get; init; } = true;

    [JsonConverter(typeof(LenientObjectJsonConverter<GlobalHotkeyChord>))]
    public GlobalHotkeyChord RevealXpOverlayTabShortcut { get; init; } =
        GlobalHotkeyChord.DefaultRevealXpOverlayTab;

    [JsonConverter(typeof(LenientObjectJsonConverter<GlobalHotkeyChord>))]
    public GlobalHotkeyChord ToggleDividerResizingShortcut { get; init; } =
        GlobalHotkeyChord.DefaultToggleDividerResizing;

    [JsonConverter(typeof(LenientObjectJsonConverter<GlobalHotkeyChord>))]
    public GlobalHotkeyChord TimerSplitShortcut { get; init; } = GlobalHotkeyChord.DefaultTimerSplit;

    [JsonConverter(typeof(LenientObjectJsonConverter<GlobalHotkeyChord>))]
    public GlobalHotkeyChord TimerFinishShortcut { get; init; } = GlobalHotkeyChord.DefaultTimerFinish;

    [JsonConverter(typeof(LenientObjectJsonConverter<GlobalHotkeyChord>))]
    public GlobalHotkeyChord TimerResetShortcut { get; init; } = GlobalHotkeyChord.DefaultTimerReset;

    [JsonConverter(typeof(LenientObjectJsonConverter<GlobalHotkeyChord>))]
    public GlobalHotkeyChord NextTabShortcut { get; init; } = GlobalHotkeyChord.DefaultNextTab;

    [JsonConverter(typeof(LenientObjectJsonConverter<GlobalHotkeyChord>))]
    public GlobalHotkeyChord PreviousTabShortcut { get; init; } = GlobalHotkeyChord.DefaultPreviousTab;

    [JsonConverter(typeof(LenientObjectJsonConverter<GlobalHotkeyChord>))]
    public GlobalHotkeyChord ToggleTheatreModeShortcut { get; init; } = GlobalHotkeyChord.DefaultToggleTheatreMode;

    public double TwoByThreeTopRowFraction { get; init; } = 0.6;

    public IReadOnlyList<PanelSplitState> SplitStates { get; init; } = Array.Empty<PanelSplitState>();

    public IReadOnlyDictionary<Guid, GameViewportSize> GameViewportSizes { get; init; } =
        new Dictionary<Guid, GameViewportSize>();

    [JsonConverter(typeof(LenientTargetLevelsJsonConverter))]
    public IReadOnlyDictionary<Guid, long> XpCalculatorTargetLevels { get; init; } = new Dictionary<Guid, long>();

    [JsonConverter(typeof(LenientListJsonConverter<OverlayCardPlacement>))]
    public IReadOnlyList<OverlayCardPlacement> OverlayCards { get; init; } = Array.Empty<OverlayCardPlacement>();

    [JsonConverter(typeof(LenientListJsonConverter<PanelTab>))]
    public IReadOnlyList<PanelTab> Tabs { get; init; } = Array.Empty<PanelTab>();

    public int ActiveTab { get; init; }

    [JsonConverter(typeof(LenientObjectJsonConverter<ToolsWindowPlacement>))]
    public ToolsWindowPlacement? ToolsWindow { get; init; }

    public SecondMonitorMode SecondMonitorMode { get; init; }

    // Migration input from settings written before overlay add-ons; validation converts it and never writes it back.
    [JsonPropertyName("xpOverlayBoundsByAccount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<Guid, OverlayBounds>? LegacyXpOverlayBoundsByAccount { get; init; }

    public static PanelSettings Default => new(PanelLayout.TwoByTwo, new Guid?[5])
    {
        ShareLinkedAccounts = true
    };
}
