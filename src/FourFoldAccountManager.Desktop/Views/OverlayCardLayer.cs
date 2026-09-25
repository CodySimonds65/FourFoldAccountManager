using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record OverlayCardModel(
    OverlayCardKey Key,
    OverlayAddOnDefinition Definition,
    OverlayBounds? Bounds,
    int CascadeIndex,
    object Data);

public sealed class OverlayCardBoundsCommittedEventArgs(OverlayCardKey key, OverlayBounds bounds) : EventArgs
{
    public OverlayCardKey Key { get; } = key;

    public OverlayBounds Bounds { get; } = bounds;
}

public sealed class OverlayCardLayer : Canvas
{
    private const int DraggingZIndex = 1000;

    private readonly Dictionary<OverlayCardKey, CardEntry> _cards = [];

    public OverlayCardLayer()
    {
        // No background: empty layer space never takes input, so a window-wide layer
        // cannot block the per-client layers or the game underneath it.
        Background = null;
        ClipToBounds = true;
        IsHitTestVisible = false;
        SizeChanged += (_, _) => ApplyAllBounds();
    }

    public event EventHandler<OverlayCardBoundsCommittedEventArgs>? BoundsCommitted;

    internal IReadOnlyDictionary<OverlayCardKey, OverlayCardFrame> Frames =>
        _cards.ToDictionary(entry => entry.Key, entry => entry.Value.Frame);

    public void SetCards(IReadOnlyList<OverlayCardModel> cards, bool editing)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var incoming = cards.ToDictionary(card => card.Key);
        foreach (var key in _cards.Keys.Where(key => !incoming.ContainsKey(key)).ToArray())
        {
            RemoveCard(key);
        }

        foreach (var model in cards)
        {
            var isNew = !_cards.TryGetValue(model.Key, out var entry);
            if (isNew)
            {
                entry = AddCard(model);
            }

            var boundsChanged = isNew || entry!.Model.Bounds != model.Bounds ||
                entry.Model.CascadeIndex != model.CascadeIndex;
            entry!.Model = model;
            entry.Frame.CardData = model.Data;
            entry.Frame.IsEditing = editing;
            Panel.SetZIndex(entry.Frame, OverlayAddOnCatalog.IndexOf(model.Key.Kind));
            if (boundsChanged)
            {
                ApplyBounds(entry);
            }
        }

        IsHitTestVisible = editing && _cards.Count > 0;
    }

    public void RestoreSavedBounds() => ApplyAllBounds();

    private CardEntry AddCard(OverlayCardModel model)
    {
        var frame = new OverlayCardFrame { Tag = model.Key };
        frame.CardDragStarted += OnCardDragStarted;
        frame.MoveDelta += OnMoveDelta;
        frame.MoveCompleted += OnManipulationCompleted;
        frame.ResizeDelta += OnResizeDelta;
        frame.ResizeCompleted += OnManipulationCompleted;
        Children.Add(frame);
        var entry = new CardEntry(frame, model);
        _cards.Add(model.Key, entry);
        return entry;
    }

    private void RemoveCard(OverlayCardKey key)
    {
        if (!_cards.Remove(key, out var entry))
        {
            return;
        }

        entry.Frame.CardDragStarted -= OnCardDragStarted;
        entry.Frame.MoveDelta -= OnMoveDelta;
        entry.Frame.MoveCompleted -= OnManipulationCompleted;
        entry.Frame.ResizeDelta -= OnResizeDelta;
        entry.Frame.ResizeCompleted -= OnManipulationCompleted;
        Children.Remove(entry.Frame);
    }

    private void ApplyAllBounds()
    {
        foreach (var entry in _cards.Values)
        {
            ApplyBounds(entry);
        }
    }

    private void ApplyBounds(CardEntry entry)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var model = entry.Model;
        var bounds = model.Bounds?.ClampToViewport() ??
            OverlayCardPolicy.DefaultBounds(model.Definition, ActualWidth, ActualHeight, model.CascadeIndex);
        var (minimumWidth, minimumHeight) = MinimumSize(entry);
        entry.Frame.MinWidth = minimumWidth;
        entry.Frame.MinHeight = minimumHeight;
        var width = Math.Clamp(bounds.Width * ActualWidth, minimumWidth, ActualWidth);
        var height = Math.Clamp(bounds.Height * ActualHeight, minimumHeight, ActualHeight);
        var left = Math.Clamp(bounds.X * ActualWidth, 0d, ActualWidth - width);
        var top = Math.Clamp(bounds.Y * ActualHeight, 0d, ActualHeight - height);
        SetGeometry(entry.Frame, left, top, width, height);
    }

    private (double Width, double Height) MinimumSize(CardEntry entry) =>
        (Math.Min(entry.Model.Definition.MinimumWidth, ActualWidth),
         Math.Min(entry.Model.Definition.MinimumHeight, ActualHeight));

    private static void SetGeometry(OverlayCardFrame frame, double left, double top, double width, double height)
    {
        frame.Width = width;
        frame.Height = height;
        SetLeft(frame, left);
        SetTop(frame, top);
    }

    private void OnCardDragStarted(object? sender, EventArgs args)
    {
        if (sender is OverlayCardFrame frame && TryGetEntry(frame, out _))
        {
            Panel.SetZIndex(frame, DraggingZIndex);
        }
    }

    private void OnMoveDelta(object sender, DragDeltaEventArgs args)
    {
        if (sender is not OverlayCardFrame frame || !TryGetEntry(frame, out _) ||
            ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var left = Math.Clamp(GetLeft(frame) + args.HorizontalChange, 0d, ActualWidth - frame.Width);
        var top = Math.Clamp(GetTop(frame) + args.VerticalChange, 0d, ActualHeight - frame.Height);
        SetGeometry(frame, left, top, frame.Width, frame.Height);
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs args)
    {
        if (sender is not OverlayCardFrame frame || !TryGetEntry(frame, out var entry) ||
            ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var (minimumWidth, minimumHeight) = MinimumSize(entry);
        var width = Math.Clamp(frame.Width + args.HorizontalChange, minimumWidth, ActualWidth);
        var height = Math.Clamp(frame.Height + args.VerticalChange, minimumHeight, ActualHeight);
        var left = Math.Clamp(GetLeft(frame), 0d, ActualWidth - width);
        var top = Math.Clamp(GetTop(frame), 0d, ActualHeight - height);
        SetGeometry(frame, left, top, width, height);
    }

    private void OnManipulationCompleted(object sender, DragCompletedEventArgs args)
    {
        // A card removed mid-drag (full screen exited, card switched off) must not save bounds.
        if (sender is not OverlayCardFrame frame || !TryGetEntry(frame, out var entry))
        {
            return;
        }

        Panel.SetZIndex(frame, OverlayAddOnCatalog.IndexOf(entry.Model.Key.Kind));
        if (args.Canceled)
        {
            ApplyBounds(entry);
            return;
        }

        if (GetVisualBounds(entry) is { } bounds)
        {
            BoundsCommitted?.Invoke(this, new OverlayCardBoundsCommittedEventArgs(entry.Model.Key, bounds));
        }
    }

    private OverlayBounds? GetVisualBounds(CardEntry entry)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var frame = entry.Frame;
        var (minimumWidth, minimumHeight) = MinimumSize(entry);
        var width = Math.Clamp(frame.Width, minimumWidth, ActualWidth);
        var height = Math.Clamp(frame.Height, minimumHeight, ActualHeight);
        var left = Math.Clamp(GetLeft(frame), 0d, ActualWidth - width);
        var top = Math.Clamp(GetTop(frame), 0d, ActualHeight - height);
        return new OverlayBounds(left / ActualWidth, top / ActualHeight, width / ActualWidth, height / ActualHeight);
    }

    private bool TryGetEntry(OverlayCardFrame frame, out CardEntry entry)
    {
        if (frame.Tag is OverlayCardKey key && _cards.TryGetValue(key, out var candidate) &&
            ReferenceEquals(candidate.Frame, frame))
        {
            entry = candidate;
            return true;
        }

        entry = null!;
        return false;
    }

    private sealed class CardEntry(OverlayCardFrame frame, OverlayCardModel model)
    {
        public OverlayCardFrame Frame { get; } = frame;

        public OverlayCardModel Model { get; set; } = model;
    }
}
