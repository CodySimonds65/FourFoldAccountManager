using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Panel;

public static class PanelLayoutPolicy
{
    private const int SlotCount = 5;

    public static GridDimensions GetDimensions(PanelLayout layout) =>
        layout switch
        {
            PanelLayout.OneByTwo => new GridDimensions(1, 2),
            PanelLayout.TwoByOne => new GridDimensions(2, 1),
            PanelLayout.TwoByTwo => new GridDimensions(2, 2),
            PanelLayout.TwoByThree => new GridDimensions(2, 3),
            PanelLayout.OneByTwoVertical => new GridDimensions(2, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown panel layout.")
        };

    public static int GetVisibleSlotCount(PanelLayout layout) =>
        GetSlotPlacements(layout).Count;

    public static IReadOnlyList<PanelSlotPlacement> GetSlotPlacements(PanelLayout layout) =>
        layout switch
        {
            PanelLayout.OneByTwo => new[]
            {
                new PanelSlotPlacement(0, 0),
                new PanelSlotPlacement(0, 1)
            },
            PanelLayout.TwoByOne => new[]
            {
                new PanelSlotPlacement(0, 0),
                new PanelSlotPlacement(1, 0)
            },
            PanelLayout.TwoByTwo => new[]
            {
                new PanelSlotPlacement(0, 0),
                new PanelSlotPlacement(0, 1),
                new PanelSlotPlacement(1, 0),
                new PanelSlotPlacement(1, 1)
            },
            PanelLayout.TwoByThree => new[]
            {
                new PanelSlotPlacement(0, 0),
                new PanelSlotPlacement(0, 1),
                new PanelSlotPlacement(1, 0),
                new PanelSlotPlacement(1, 1),
                new PanelSlotPlacement(1, 2)
            },
            PanelLayout.OneByTwoVertical => new[]
            {
                new PanelSlotPlacement(0, 0, 2),
                new PanelSlotPlacement(0, 1),
                new PanelSlotPlacement(1, 1)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown panel layout.")
        };

    public static PanelSettings WithLayout(PanelSettings settings, PanelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _ = GetDimensions(layout);
        return new PanelSettings(layout, settings.SlotAccountIds)
        {
            FillGameToPanel = settings.FillGameToPanel,
            ShowFullScreenExitButton = settings.ShowFullScreenExitButton,
            TwoByThreeTopRowFraction = settings.TwoByThreeTopRowFraction,
            GameViewportSizes = settings.GameViewportSizes
        };
    }

    public static PanelSettings Assign(PanelSettings settings, int slotIndex, Guid? accountId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (slotIndex is < 0 or >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Slot index must be between 0 and 4.");
        }

        if (settings.SlotAccountIds.Count != SlotCount)
        {
            throw new ArgumentException("Panel settings must contain exactly five slot assignments.", nameof(settings));
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
        return new PanelSettings(settings.Layout, assignments)
        {
            FillGameToPanel = settings.FillGameToPanel,
            ShowFullScreenExitButton = settings.ShowFullScreenExitButton,
            TwoByThreeTopRowFraction = settings.TwoByThreeTopRowFraction,
            GameViewportSizes = settings.GameViewportSizes
        };
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
            throw new ArgumentException("Panel settings must contain exactly five slot assignments.", nameof(settings));
        }

        var assignments = settings.SlotAccountIds
            .Select(assignedId => assignedId == accountId ? null : assignedId)
            .ToArray();
        var viewportSizes = new Dictionary<Guid, GameViewportSize>(settings.GameViewportSizes);
        viewportSizes.Remove(accountId);
        return new PanelSettings(settings.Layout, assignments)
        {
            FillGameToPanel = settings.FillGameToPanel,
            ShowFullScreenExitButton = settings.ShowFullScreenExitButton,
            TwoByThreeTopRowFraction = settings.TwoByThreeTopRowFraction,
            GameViewportSizes = viewportSizes
        };
    }

    public static GameViewportSize GetGameViewportSize(PanelSettings settings, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        return settings.GameViewportSizes.TryGetValue(accountId, out var size)
            ? size
            : GameViewportSize.Default;
    }

    public static PanelSettings WithGameViewportSize(
        PanelSettings settings,
        Guid accountId,
        GameViewportSize size)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(size);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        if (!size.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Viewport dimensions must be between 25% and 100%.");
        }

        var viewportSizes = new Dictionary<Guid, GameViewportSize>(settings.GameViewportSizes)
        {
            [accountId] = size
        };
        return settings with { GameViewportSizes = viewportSizes };
    }
}
