using System.Diagnostics.CodeAnalysis;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Overlay;

public enum OverlayAddOnScope
{
    Account,
    Global
}

public sealed record OverlayAddOnDefinition(
    OverlayAddOnKind Kind,
    OverlayAddOnScope Scope,
    string DisplayName,
    double DefaultWidth,
    double DefaultHeight,
    double MinimumWidth,
    double MinimumHeight);

public static class OverlayAddOnCatalog
{
    // Catalog order is also the stacking order of cards within a layer.
    public static IReadOnlyList<OverlayAddOnDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new OverlayAddOnDefinition(OverlayAddOnKind.Xp, OverlayAddOnScope.Account, "XP/hr", 200, 52, 144, 40)
    });

    public static bool TryGet(OverlayAddOnKind kind, [NotNullWhen(true)] out OverlayAddOnDefinition? definition)
    {
        definition = All.FirstOrDefault(candidate => candidate.Kind == kind);
        return definition is not null;
    }

    public static int IndexOf(OverlayAddOnKind kind)
    {
        for (var index = 0; index < All.Count; index++)
        {
            if (All[index].Kind == kind)
            {
                return index;
            }
        }

        return -1;
    }
}
