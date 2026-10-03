using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record FloatingCardModel(
    OverlayCardKey Key,
    OverlayAddOnDefinition Definition,
    FloatingCardBounds? Bounds,
    object Data);

// Keeps one FloatingCardWindow per floating card, adding, updating, and closing windows to match each refresh.
public sealed class FloatingCardHost
{
    private readonly Dictionary<OverlayCardKey, FloatingCardWindow> _windows = [];
    private bool _hidden;

    public event EventHandler<FloatingCardBoundsCommittedEventArgs>? BoundsCommitted;

    public void Update(IReadOnlyList<FloatingCardModel> cards, bool arranging)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var incoming = cards.Select(card => card.Key).ToHashSet();
        foreach (var key in _windows.Keys.Where(key => !incoming.Contains(key)).ToArray())
        {
            Close(key);
        }

        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            if (!_windows.TryGetValue(card.Key, out var window))
            {
                // The default spot cascades by how many cards already float, so a new card never lands on an older one.
                window = new FloatingCardWindow(card.Key, card.Definition, _windows.Count);
                window.Place(card.Bounds);
                window.BoundsCommitted += Window_BoundsCommitted;
                _windows.Add(card.Key, window);
                window.SetCard(card.Data, arranging);
                if (!_hidden)
                {
                    window.Show();
                }

                continue;
            }

            window.SetCard(card.Data, arranging);
        }
    }

    // Puts a card back at its saved spot after its new position could not be saved.
    public void Restore(OverlayCardKey key, FloatingCardBounds? bounds)
    {
        if (_windows.TryGetValue(key, out var window))
        {
            window.Place(bounds);
        }
    }

    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        foreach (var window in _windows.Values)
        {
            if (hidden)
            {
                window.Hide();
            }
            else
            {
                window.Show();
            }
        }
    }

    public void CloseAll()
    {
        foreach (var key in _windows.Keys.ToArray())
        {
            Close(key);
        }
    }

    private void Close(OverlayCardKey key)
    {
        if (_windows.Remove(key, out var window))
        {
            window.BoundsCommitted -= Window_BoundsCommitted;
            window.Close();
        }
    }

    private void Window_BoundsCommitted(object? sender, FloatingCardBoundsCommittedEventArgs args) =>
        BoundsCommitted?.Invoke(this, args);
}
