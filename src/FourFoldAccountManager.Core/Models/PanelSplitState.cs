namespace FourFoldAccountManager.Core.Models;

public enum PanelSplitOrientation
{
    Horizontal,
    Vertical
}

public sealed record PanelSplitState(string Id, IReadOnlyList<double> Weights);
