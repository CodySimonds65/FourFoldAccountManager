using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public partial class ShortcutRow : UserControl
{
    public ShortcutRow()
    {
        InitializeComponent();
    }

    public event EventHandler? CaptureRequested;

    public GlobalShortcutAction Action { get; set; }

    public string Title
    {
        get => TitleText.Text;
        set
        {
            TitleText.Text = value;
            AutomationProperties.SetName(CaptureButton, $"{value} shortcut");
        }
    }

    public string Description
    {
        get => DescriptionText.Text;
        set => DescriptionText.Text = value;
    }

    internal void SetKeysText(string text) => CaptureButton.Content = text;

    internal void SetStatus(string status) => StatusText.Text = status;

    private void CaptureButton_Click(object sender, RoutedEventArgs e) =>
        CaptureRequested?.Invoke(this, EventArgs.Empty);
}
