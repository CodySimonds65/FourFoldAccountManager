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

    public bool ShowFullScreenExitButton { get; init; } = true;

    public IReadOnlyDictionary<Guid, GameViewportSize> GameViewportSizes { get; init; } =
        new Dictionary<Guid, GameViewportSize>();

    public static PanelSettings Default => new(PanelLayout.TwoByTwo, new Guid?[4]);
}
