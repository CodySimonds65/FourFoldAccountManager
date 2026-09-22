using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FourFoldAccountManager.Desktop.Views;

public partial class XpOverlayCard : UserControl
{
    public static readonly DependencyProperty AccountLabelProperty = DependencyProperty.Register(
        nameof(AccountLabel), typeof(string), typeof(XpOverlayCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty XpPerHourTextProperty = DependencyProperty.Register(
        nameof(XpPerHourText), typeof(string), typeof(XpOverlayCard), new PropertyMetadata("— XP/hr"));

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(XpOverlayCard), new PropertyMetadata(false, OnIsEditingChanged));

    public XpOverlayCard()
    {
        InitializeComponent();
        MoveThumb.DragDelta += (_, args) => MoveDelta?.Invoke(this, args);
        MoveThumb.DragCompleted += (_, args) => MoveCompleted?.Invoke(this, args);
        ResizeThumb.DragDelta += (_, args) => ResizeDelta?.Invoke(this, args);
        ResizeThumb.DragCompleted += (_, args) => ResizeCompleted?.Invoke(this, args);
    }

    public event DragDeltaEventHandler? MoveDelta;

    public event DragCompletedEventHandler? MoveCompleted;

    public event DragDeltaEventHandler? ResizeDelta;

    public event DragCompletedEventHandler? ResizeCompleted;

    public string AccountLabel
    {
        get => (string)GetValue(AccountLabelProperty);
        set => SetValue(AccountLabelProperty, value);
    }

    public string XpPerHourText
    {
        get => (string)GetValue(XpPerHourTextProperty);
        set => SetValue(XpPerHourTextProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    private static void OnIsEditingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is XpOverlayCard card)
        {
            card.IsHitTestVisible = (bool)args.NewValue;
        }
    }
}
