using System.Text.Json.Serialization;

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

    public bool PluginsSidebarExpanded { get; init; } = true;

    public GlobalHotkeyChord RevealXpOverlayTabShortcut { get; init; } =
        GlobalHotkeyChord.DefaultRevealXpOverlayTab;

    public GlobalHotkeyChord ToggleDividerResizingShortcut { get; init; } =
        GlobalHotkeyChord.DefaultToggleDividerResizing;

    [JsonConverter(typeof(LenientHotkeyChordJsonConverter))]
    public GlobalHotkeyChord TimerSplitShortcut { get; init; } = GlobalHotkeyChord.DefaultTimerSplit;

    [JsonConverter(typeof(LenientHotkeyChordJsonConverter))]
    public GlobalHotkeyChord TimerFinishShortcut { get; init; } = GlobalHotkeyChord.DefaultTimerFinish;

    [JsonConverter(typeof(LenientHotkeyChordJsonConverter))]
    public GlobalHotkeyChord TimerResetShortcut { get; init; } = GlobalHotkeyChord.DefaultTimerReset;

    public double TwoByThreeTopRowFraction { get; init; } = 0.6;

    public IReadOnlyList<PanelSplitState> SplitStates { get; init; } = Array.Empty<PanelSplitState>();

    public IReadOnlyDictionary<Guid, GameViewportSize> GameViewportSizes { get; init; } =
        new Dictionary<Guid, GameViewportSize>();

    [JsonConverter(typeof(LenientTargetLevelsJsonConverter))]
    public IReadOnlyDictionary<Guid, long> XpCalculatorTargetLevels { get; init; } = new Dictionary<Guid, long>();

    [JsonConverter(typeof(OverlayCardListJsonConverter))]
    public IReadOnlyList<OverlayCardPlacement> OverlayCards { get; init; } = Array.Empty<OverlayCardPlacement>();

    // Migration input from settings written before overlay add-ons; validation converts it and never writes it back.
    [JsonPropertyName("xpOverlayBoundsByAccount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<Guid, OverlayBounds>? LegacyXpOverlayBoundsByAccount { get; init; }

    public static PanelSettings Default => new(PanelLayout.TwoByTwo, new Guid?[5])
    {
        ShareLinkedAccounts = true
    };
}
