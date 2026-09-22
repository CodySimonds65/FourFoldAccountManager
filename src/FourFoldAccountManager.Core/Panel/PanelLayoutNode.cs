using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Panel;

public abstract record PanelLayoutNode;

public sealed record PanelSlotNode(int SlotIndex) : PanelLayoutNode;

public sealed record PanelSplitNode : PanelLayoutNode
{
    public PanelSplitNode(
        string id,
        PanelSplitOrientation orientation,
        IReadOnlyList<PanelLayoutNode> children)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count < 2)
        {
            throw new ArgumentException("A split must contain at least two children.", nameof(children));
        }

        Id = id;
        Orientation = orientation;
        Children = Array.AsReadOnly(children.ToArray());
    }

    public string Id { get; }

    public PanelSplitOrientation Orientation { get; }

    public IReadOnlyList<PanelLayoutNode> Children { get; }
}
