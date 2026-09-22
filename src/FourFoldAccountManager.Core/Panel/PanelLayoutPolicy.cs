using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Panel;

public static class PanelLayoutPolicy
{
    private const int SlotCount = 5;
    private const double MinimumSplitWeight = 0.30;

    private static readonly IReadOnlyList<PanelSplitState> DefaultSplitStates = Array.AsReadOnly(new[]
    {
        CreateSplitState("1x2.columns", 0.5, 0.5),
        CreateSplitState("2x1.rows", 0.5, 0.5),
        CreateSplitState("2x2.rows", 0.5, 0.5),
        CreateSplitState("2x2.top", 0.5, 0.5),
        CreateSplitState("2x2.bottom", 0.5, 0.5),
        CreateSplitState("2x3.rows", 0.6, 0.4),
        CreateSplitState("2x3.top", 0.5, 0.5),
        CreateSplitState("2x3.bottom", 1d / 3, 1d / 3, 1d / 3),
        CreateSplitState("1x3.rows", 0.6, 0.4),
        CreateSplitState("1x3.bottom", 1d / 3, 1d / 3, 1d / 3),
        CreateSplitState("1x2v.columns", 0.5, 0.5),
        CreateSplitState("1x2v.right.rows", 0.5, 0.5)
    });

    public static PanelLayoutNode GetLayoutTree(PanelLayout layout) =>
        layout switch
        {
            PanelLayout.OneByTwo => Split("1x2.columns", PanelSplitOrientation.Horizontal, Slot(0), Slot(1)),
            PanelLayout.TwoByOne => Split("2x1.rows", PanelSplitOrientation.Vertical, Slot(0), Slot(1)),
            PanelLayout.TwoByTwo => Split(
                "2x2.rows",
                PanelSplitOrientation.Vertical,
                Split("2x2.top", PanelSplitOrientation.Horizontal, Slot(0), Slot(1)),
                Split("2x2.bottom", PanelSplitOrientation.Horizontal, Slot(2), Slot(3))),
            PanelLayout.TwoByThree => Split(
                "2x3.rows",
                PanelSplitOrientation.Vertical,
                Split("2x3.top", PanelSplitOrientation.Horizontal, Slot(0), Slot(1)),
                Split("2x3.bottom", PanelSplitOrientation.Horizontal, Slot(2), Slot(3), Slot(4))),
            PanelLayout.OneByTwoVertical => Split(
                "1x2v.columns",
                PanelSplitOrientation.Horizontal,
                Slot(0),
                Split("1x2v.right.rows", PanelSplitOrientation.Vertical, Slot(1), Slot(2))),
            PanelLayout.OneByOne => Slot(0),
            PanelLayout.OneByThree => Split(
                "1x3.rows",
                PanelSplitOrientation.Vertical,
                Slot(0),
                Split("1x3.bottom", PanelSplitOrientation.Horizontal, Slot(1), Slot(2), Slot(3))),
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown panel layout.")
        };

    public static IReadOnlyList<PanelSplitState> GetDefaultSplitStates() => DefaultSplitStates;

    public static PanelSplitState GetSplitState(PanelSettings settings, string id)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaultState = GetDefaultSplitState(id);
        var state = settings.SplitStates?.SingleOrDefault(candidate => candidate.Id == id);
        return state is null ? defaultState : state;
    }

    public static PanelSettings WithSplitState(PanelSettings settings, PanelSplitState state)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(state);
        var normalizedState = NormalizeAndValidateSplitState(state, GetDefaultSplitState(state.Id));
        var states = (settings.SplitStates ?? Array.Empty<PanelSplitState>())
            .Where(existing => existing.Id != state.Id)
            .Append(normalizedState)
            .ToArray();

        return CopySettings(settings, states);
    }

    public static PanelSettings ResetSplitStates(PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return CopySettings(settings, GetDefaultSplitStates());
    }

    public static GridDimensions GetDimensions(PanelLayout layout) =>
        layout switch
        {
            PanelLayout.OneByTwo => new GridDimensions(1, 2),
            PanelLayout.TwoByOne => new GridDimensions(2, 1),
            PanelLayout.TwoByTwo => new GridDimensions(2, 2),
            // Six grid units let the top slots span three each and the bottom slots span two each.
            PanelLayout.TwoByThree => new GridDimensions(2, 6),
            PanelLayout.OneByTwoVertical => new GridDimensions(2, 2),
            PanelLayout.OneByOne => new GridDimensions(1, 1),
            PanelLayout.OneByThree => new GridDimensions(2, 3),
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
                new PanelSlotPlacement(0, 0, ColumnSpan: 3),
                new PanelSlotPlacement(0, 3, ColumnSpan: 3),
                new PanelSlotPlacement(1, 0, ColumnSpan: 2),
                new PanelSlotPlacement(1, 2, ColumnSpan: 2),
                new PanelSlotPlacement(1, 4, ColumnSpan: 2)
            },
            PanelLayout.OneByTwoVertical => new[]
            {
                new PanelSlotPlacement(0, 0, 2),
                new PanelSlotPlacement(0, 1),
                new PanelSlotPlacement(1, 1)
            },
            PanelLayout.OneByOne => new[]
            {
                new PanelSlotPlacement(0, 0)
            },
            PanelLayout.OneByThree => new[]
            {
                new PanelSlotPlacement(0, 0, ColumnSpan: 3),
                new PanelSlotPlacement(1, 0),
                new PanelSlotPlacement(1, 1),
                new PanelSlotPlacement(1, 2)
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
            SplitStates = settings.SplitStates,
            GameViewportSizes = settings.GameViewportSizes,
            XpOverlayBoundsByAccount = settings.XpOverlayBoundsByAccount
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
            SplitStates = settings.SplitStates,
            GameViewportSizes = settings.GameViewportSizes,
            XpOverlayBoundsByAccount = settings.XpOverlayBoundsByAccount
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
        var overlayBounds = new Dictionary<Guid, XpOverlayBounds>(settings.XpOverlayBoundsByAccount);
        overlayBounds.Remove(accountId);
        return new PanelSettings(settings.Layout, assignments)
        {
            FillGameToPanel = settings.FillGameToPanel,
            ShowFullScreenExitButton = settings.ShowFullScreenExitButton,
            TwoByThreeTopRowFraction = settings.TwoByThreeTopRowFraction,
            SplitStates = settings.SplitStates,
            GameViewportSizes = viewportSizes,
            XpOverlayBoundsByAccount = overlayBounds
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

    public static XpOverlayBounds? GetXpOverlayBounds(PanelSettings settings, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        return settings.XpOverlayBoundsByAccount.TryGetValue(accountId, out var bounds)
            ? bounds
            : null;
    }

    public static PanelSettings WithXpOverlayBounds(
        PanelSettings settings,
        Guid accountId,
        XpOverlayBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(bounds);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        if (!bounds.IsValid)
        {
            throw new ArgumentException("Overlay bounds must be valid and within the normalized viewport.", nameof(bounds));
        }

        var overlayBounds = new Dictionary<Guid, XpOverlayBounds>(settings.XpOverlayBoundsByAccount)
        {
            [accountId] = bounds
        };
        return settings with { XpOverlayBoundsByAccount = overlayBounds };
    }

    private static PanelSlotNode Slot(int index) => new(index);

    private static PanelSplitNode Split(
        string id,
        PanelSplitOrientation orientation,
        params PanelLayoutNode[] children) => new(id, orientation, children);

    private static PanelSplitState CreateSplitState(string id, params double[] weights) =>
        new(id, Array.AsReadOnly(weights));

    private static PanelSplitState GetDefaultSplitState(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return DefaultSplitStates.SingleOrDefault(state => state.Id == id)
            ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown split state ID.");
    }

    internal static PanelSplitState NormalizeAndValidateSplitState(PanelSplitState state, PanelSplitState expectedState)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedState);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Id);
        ArgumentNullException.ThrowIfNull(state.Weights);
        if (state.Weights.Count != expectedState.Weights.Count)
        {
            throw new ArgumentException("Split state has the wrong track count.", nameof(state));
        }

        var normalized = PanelSplitMath.Normalize(state.Weights);
        if (normalized.Any(weight => weight < MinimumSplitWeight))
        {
            throw new ArgumentException("Split state weights must satisfy the minimum track size.", nameof(state));
        }

        return new PanelSplitState(state.Id, Array.AsReadOnly(normalized.ToArray()));
    }

    private static PanelSettings CopySettings(PanelSettings settings, IReadOnlyList<PanelSplitState> splitStates) =>
        new(settings.Layout, settings.SlotAccountIds)
        {
            FillGameToPanel = settings.FillGameToPanel,
            ShowFullScreenExitButton = settings.ShowFullScreenExitButton,
            TwoByThreeTopRowFraction = settings.TwoByThreeTopRowFraction,
            SplitStates = Array.AsReadOnly(splitStates.Select(CloneSplitState).ToArray()),
            GameViewportSizes = settings.GameViewportSizes,
            XpOverlayBoundsByAccount = settings.XpOverlayBoundsByAccount
        };

    private static PanelSplitState CloneSplitState(PanelSplitState state) =>
        new(state.Id, Array.AsReadOnly(state.Weights.ToArray()));
}
