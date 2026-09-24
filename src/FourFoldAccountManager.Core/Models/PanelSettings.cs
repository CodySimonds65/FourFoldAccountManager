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

    public GlobalHotkeyChord RevealXpOverlayTabShortcut { get; init; } =
        GlobalHotkeyChord.DefaultRevealXpOverlayTab;

    public GlobalHotkeyChord ToggleDividerResizingShortcut { get; init; } =
        GlobalHotkeyChord.DefaultToggleDividerResizing;

    public double TwoByThreeTopRowFraction { get; init; } = 0.6;

    public IReadOnlyList<PanelSplitState> SplitStates { get; init; } = Array.Empty<PanelSplitState>();

    public IReadOnlyDictionary<Guid, GameViewportSize> GameViewportSizes { get; init; } =
        new Dictionary<Guid, GameViewportSize>();

    public IReadOnlyDictionary<Guid, XpOverlayBounds> XpOverlayBoundsByAccount { get; init; } =
        new Dictionary<Guid, XpOverlayBounds>();

    public static PanelSettings Default => new(PanelLayout.TwoByTwo, new Guid?[5])
    {
        ShareLinkedAccounts = true
    };
}
