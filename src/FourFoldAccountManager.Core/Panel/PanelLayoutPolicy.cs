using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Panel;

public static class PanelLayoutPolicy
{
    private const int SlotCount = 4;

    public static GridDimensions GetDimensions(PanelLayout layout) =>
        layout switch
        {
            PanelLayout.OneByTwo => new GridDimensions(1, 2),
            PanelLayout.TwoByOne => new GridDimensions(2, 1),
            PanelLayout.TwoByTwo => new GridDimensions(2, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown panel layout.")
        };

    public static int GetVisibleSlotCount(PanelLayout layout)
    {
        var dimensions = GetDimensions(layout);
        return dimensions.Rows * dimensions.Columns;
    }

    public static PanelSettings WithLayout(PanelSettings settings, PanelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = GetDimensions(layout);
        return new PanelSettings(layout, settings.SlotAccountIds);
    }

    public static PanelSettings Assign(PanelSettings settings, int slotIndex, Guid? accountId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (slotIndex is < 0 or >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Slot index must be between 0 and 3.");
        }

        if (settings.SlotAccountIds.Count != SlotCount)
        {
            throw new ArgumentException("Panel settings must contain exactly four slot assignments.", nameof(settings));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An assigned account ID cannot be empty.", nameof(accountId));
        }

        var assignments = settings.SlotAccountIds.ToArray();
        if (accountId is not null)
        {
            for (var index = 0; index < assignments.Length; index++)
            {
                if (assignments[index] == accountId)
                {
                    assignments[index] = null;
                }
            }
        }

        assignments[slotIndex] = accountId;
        return new PanelSettings(settings.Layout, assignments);
    }

    public static PanelSettings ClearAccount(PanelSettings settings, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID cannot be empty.", nameof(accountId));
        }

        if (settings.SlotAccountIds.Count != SlotCount)
        {
            throw new ArgumentException("Panel settings must contain exactly four slot assignments.", nameof(settings));
        }

        var assignments = settings.SlotAccountIds
            .Select(assignedId => assignedId == accountId ? null : assignedId)
            .ToArray();
        return new PanelSettings(settings.Layout, assignments);
    }
}
