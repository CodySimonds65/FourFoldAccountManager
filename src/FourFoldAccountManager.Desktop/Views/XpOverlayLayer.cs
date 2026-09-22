using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public sealed class XpOverlayLayer : Canvas
{
    public const string AccountDragDataFormat = "FourFold.XpOverlayAccountId";

    public const double DefaultCardWidth = 200;

    public const double DefaultCardHeight = 52;

    public const double MinimumCardWidth = 144;

    public const double MinimumCardHeight = 40;

    private Guid? _accountId;
    private XpOverlayBounds? _settingsBounds;
    private XpOverlayCard? _card;
    private bool _editing;

    public XpOverlayLayer()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        AllowDrop = false;
        IsHitTestVisible = false;
        SizeChanged += (_, _) => ApplySettingsBounds();
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    public event EventHandler<XpOverlayAccountDroppedEventArgs>? AccountDropped;

    public event EventHandler<XpOverlayBoundsCommittedEventArgs>? BoundsCommitted;

    public void SetSlot(
        Guid? accountId,
        string accountLabel,
        string xpPerHourText,
        XpOverlayBounds? bounds,
        bool editing)
    {
        var accountChanged = _accountId != accountId;
        _accountId = accountId;
        _editing = editing;
        IsHitTestVisible = editing && accountId is not null;
        AllowDrop = editing && accountId is not null;

        if (accountId is null || bounds is null)
        {
            RemoveCard();
            _settingsBounds = null;
            return;
        }

        var safeBounds = bounds.ClampToViewport();
        var boundsChanged = _settingsBounds != safeBounds;
        if (_card is null || accountChanged)
        {
            RemoveCard();
            CreateCard();
            boundsChanged = true;
        }

        _settingsBounds = safeBounds;
        _card!.AccountLabel = accountLabel;
        _card.XpPerHourText = xpPerHourText;
        _card.IsEditing = editing;
        _card.IsHitTestVisible = editing;
        if (boundsChanged)
        {
            ApplySettingsBounds();
        }
    }

    public XpOverlayBounds CreateDefaultBoundsAt(Point normalizedDropPoint)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            throw new InvalidOperationException("The overlay layer must have a measured viewport before placing an overlay.");
        }

        var width = Math.Min(DefaultCardWidth, ActualWidth) / ActualWidth;
        var height = Math.Min(DefaultCardHeight, ActualHeight) / ActualHeight;
        var centerX = Math.Clamp(normalizedDropPoint.X, 0d, 1d);
        var centerY = Math.Clamp(normalizedDropPoint.Y, 0d, 1d);
        return new XpOverlayBounds(
            Math.Clamp(centerX - width / 2d, 0d, 1d - width),
            Math.Clamp(centerY - height / 2d, 0d, 1d - height),
            width,
            height);
    }

    public void RestoreSavedBounds()
    {
        ApplySettingsBounds();
    }

    private void CreateCard()
    {
        _card = new XpOverlayCard();
        _card.MoveDelta += OnMoveDelta;
        _card.MoveCompleted += OnMoveCompleted;
        _card.ResizeDelta += OnResizeDelta;
        _card.ResizeCompleted += OnResizeCompleted;
        Children.Add(_card);
    }

    private void RemoveCard()
    {
        if (_card is null)
        {
            return;
        }

        _card.MoveDelta -= OnMoveDelta;
        _card.MoveCompleted -= OnMoveCompleted;
        _card.ResizeDelta -= OnResizeDelta;
        _card.ResizeCompleted -= OnResizeCompleted;
        Children.Remove(_card);
        _card = null;
    }

    private void ApplySettingsBounds()
    {
        if (_card is null || _settingsBounds is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var minimumWidth = Math.Min(MinimumCardWidth, ActualWidth);
        var minimumHeight = Math.Min(MinimumCardHeight, ActualHeight);
        _card.MinWidth = minimumWidth;
        _card.MinHeight = minimumHeight;
        var width = Math.Clamp(_settingsBounds.Width * ActualWidth, minimumWidth, ActualWidth);
        var height = Math.Clamp(_settingsBounds.Height * ActualHeight, minimumHeight, ActualHeight);
        var left = Math.Clamp(_settingsBounds.X * ActualWidth, 0d, ActualWidth - width);
        var top = Math.Clamp(_settingsBounds.Y * ActualHeight, 0d, ActualHeight - height);
        SetCardGeometry(left, top, width, height);
    }

    private void SetCardGeometry(double left, double top, double width, double height)
    {
        if (_card is null)
        {
            return;
        }

        _card.Width = width;
        _card.Height = height;
        SetLeft(_card, left);
        SetTop(_card, top);
    }

    private void OnMoveDelta(object sender, DragDeltaEventArgs args)
    {
        if (_card is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var width = _card.Width;
        var height = _card.Height;
        var left = Math.Clamp(GetLeft(_card) + args.HorizontalChange, 0d, ActualWidth - width);
        var top = Math.Clamp(GetTop(_card) + args.VerticalChange, 0d, ActualHeight - height);
        SetCardGeometry(left, top, width, height);
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs args)
    {
        if (_card is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var minimumWidth = Math.Min(MinimumCardWidth, ActualWidth);
        var minimumHeight = Math.Min(MinimumCardHeight, ActualHeight);
        var width = Math.Clamp(_card.Width + args.HorizontalChange, minimumWidth, ActualWidth);
        var height = Math.Clamp(_card.Height + args.VerticalChange, minimumHeight, ActualHeight);
        var left = Math.Clamp(GetLeft(_card), 0d, ActualWidth - width);
        var top = Math.Clamp(GetTop(_card), 0d, ActualHeight - height);
        _card.MinWidth = minimumWidth;
        _card.MinHeight = minimumHeight;
        SetCardGeometry(left, top, width, height);
    }

    private void OnMoveCompleted(object sender, DragCompletedEventArgs args) => CompleteManipulation(args);

    private void OnResizeCompleted(object sender, DragCompletedEventArgs args) => CompleteManipulation(args);

    private void CompleteManipulation(DragCompletedEventArgs args)
    {
        if (_card is null || _accountId is null)
        {
            return;
        }

        if (args.Canceled)
        {
            ApplySettingsBounds();
            return;
        }

        var bounds = GetVisualBounds();
        if (bounds is not null)
        {
            BoundsCommitted?.Invoke(this, new XpOverlayBoundsCommittedEventArgs(_accountId.Value, bounds));
        }
    }

    private XpOverlayBounds? GetVisualBounds()
    {
        if (_card is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var width = Math.Clamp(_card.Width, Math.Min(MinimumCardWidth, ActualWidth), ActualWidth);
        var height = Math.Clamp(_card.Height, Math.Min(MinimumCardHeight, ActualHeight), ActualHeight);
        var left = Math.Clamp(GetLeft(_card), 0d, ActualWidth - width);
        var top = Math.Clamp(GetTop(_card), 0d, ActualHeight - height);
        return new XpOverlayBounds(left / ActualWidth, top / ActualHeight, width / ActualWidth, height / ActualHeight);
    }

    private void OnDragOver(object sender, DragEventArgs args)
    {
        args.Effects = _editing && TryGetMatchingAccount(args.Data, out _)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        args.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs args)
    {
        if (!_editing || _accountId is not { } accountId || !TryGetMatchingAccount(args.Data, out var draggedAccountId) ||
            ActualWidth <= 0 || ActualHeight <= 0)
        {
            args.Effects = DragDropEffects.None;
            args.Handled = true;
            return;
        }

        var location = args.GetPosition(this);
        var normalizedPoint = new Point(
            Math.Clamp(location.X / ActualWidth, 0d, 1d),
            Math.Clamp(location.Y / ActualHeight, 0d, 1d));
        AccountDropped?.Invoke(this, new XpOverlayAccountDroppedEventArgs(accountId, normalizedPoint));
        args.Effects = DragDropEffects.Move;
        args.Handled = true;
    }

    private bool TryGetMatchingAccount(IDataObject data, out Guid accountId)
    {
        accountId = Guid.Empty;
        return _accountId is { } assignedAccountId &&
            data.GetDataPresent(AccountDragDataFormat) &&
            data.GetData(AccountDragDataFormat) is Guid draggedAccountId &&
            draggedAccountId == assignedAccountId &&
            (accountId = draggedAccountId) != Guid.Empty;
    }
}

public sealed class XpOverlayAccountDroppedEventArgs(Guid accountId, Point normalizedDropPoint) : EventArgs
{
    public Guid AccountId { get; } = accountId;

    public Point NormalizedDropPoint { get; } = normalizedDropPoint;
}

public sealed class XpOverlayBoundsCommittedEventArgs(Guid accountId, XpOverlayBounds bounds) : EventArgs
{
    public Guid AccountId { get; } = accountId;

    public XpOverlayBounds Bounds { get; } = bounds;
}
