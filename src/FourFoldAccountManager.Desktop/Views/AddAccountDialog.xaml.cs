using System.Windows;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public partial class AddAccountDialog : Window
{
    public AddAccountDialog(
        string heading,
        string prompt,
        string initialLabel = "",
        string initialUsername = "",
        string initialPassword = "")
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        DialogHeading.Text = heading;
        DialogPrompt.Text = prompt;
        LabelBox.Text = initialLabel;
        UsernameBox.Text = initialUsername;
        PasswordBox.Password = initialPassword;
        LabelBox.SelectAll();
        Loaded += (_, _) => LabelBox.Focus();
        Closed += (_, _) => PasswordBox.Clear();
    }

    public string AccountLabel { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public string Password { get; private set; } = string.Empty;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Username = UsernameBox.Text.Trim();
        Password = PasswordBox.Password;
        if ((Username.Length == 0) != (Password.Length == 0))
        {
            ValidationText.Text = "Enter both a username and password, or leave both blank.";
            if (Username.Length == 0)
            {
                UsernameBox.Focus();
            }
            else
            {
                PasswordBox.Focus();
            }

            return;
        }

        try
        {
            AccountLabel = AccountProfileRules.NormalizeLabel(LabelBox.Text);
            DialogResult = true;
        }
        catch (ArgumentException exception)
        {
            ValidationText.Text = exception.Message;
            LabelBox.Focus();
            LabelBox.SelectAll();
        }
    }
}
