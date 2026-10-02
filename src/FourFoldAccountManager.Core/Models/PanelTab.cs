using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// One client tab. It carries a layout and a slot list so a tab can later hold a whole workspace; for now
// every tab is OneByOne with its account in the first slot.
public sealed record PanelTab(PanelLayout Layout, IReadOnlyList<Guid?> SlotAccountIds)
{
    [JsonIgnore]
    public Guid? AccountId => SlotAccountIds is { Count: > 0 } ? SlotAccountIds[0] : null;

    public static PanelTab ForAccount(Guid accountId) => new(PanelLayout.OneByOne, [accountId]);
}
