using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FourFoldAccountManager.Desktop.Views;

public partial class OverlayCardFrame : UserControl
{
    public static readonly DependencyProperty CardDataProperty = DependencyProperty.Register(
        nameof(CardData), typeof(object), typeof(OverlayCardFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(OverlayCardFrame), new PropertyMetadata(false, OnIsEditingChanged));

    public OverlayCardFrame()
    {
        InitializeComponent();
        MoveThumb.DragStarted += (_, _) => CardDragStarted?.Invoke(this, EventArgs.Empty);
        MoveThumb.DragDelta += (_, args) => MoveDelta?.Invoke(this, args);
        MoveThumb.DragCompleted += (_, args) => MoveCompleted?.Invoke(this, args);
        ResizeThumb.DragStarted += (_, _) => CardDragStarted?.Invoke(this, EventArgs.Empty);
        ResizeThumb.DragDelta += (_, args) => ResizeDelta?.Invoke(this, args);
        ResizeThumb.DragCompleted += (_, args) => ResizeCompleted?.Invoke(this, args);
    }

    public event EventHandler? CardDragStarted;

    public event DragDeltaEventHandler? MoveDelta;

    public event DragCompletedEventHandler? MoveCompleted;

    public event DragDeltaEventHandler? ResizeDelta;

    public event DragCompletedEventHandler? ResizeCompleted;

    public object? CardData
    {
        get => GetValue(CardDataProperty);
        set => SetValue(CardDataProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    private static void OnIsEditingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is OverlayCardFrame frame)
        {
            frame.IsHitTestVisible = (bool)args.NewValue;
        }
    }
}
