using System.Windows;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Views;

public partial class AddAccountDialog : Window
{
    private readonly Func<int, string, Task<bool>>? _verifyPlayer;
    private readonly string? _initialResolvedUsername;
    private readonly int? _initialPlayerId;

    public AddAccountDialog(
        string heading,
        string prompt,
        string initialLabel = "",
        string initialUsername = "",
        string initialPassword = "",
        string? initialRankingUsername = null,
        int? initialPlayerId = null,
        Func<int, string, Task<bool>>? verifyPlayer = null)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        DialogHeading.Text = heading;
        DialogPrompt.Text = prompt;
        LabelBox.Text = initialLabel;
        UsernameBox.Text = initialUsername;
        PasswordBox.Password = initialPassword;
        RankingUsernameBox.Text = initialRankingUsername ?? string.Empty;
        PlayerProfileBox.Text = initialPlayerId?.ToString() ?? string.Empty;
        _verifyPlayer = verifyPlayer;
        _initialResolvedUsername = initialRankingUsername ?? (initialUsername.Length > 0 ? initialUsername : null);
        _initialPlayerId = initialPlayerId;
        LabelBox.SelectAll();
        Loaded += (_, _) => LabelBox.Focus();
        Closed += (_, _) => PasswordBox.Clear();
    }

    public string AccountLabel { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public string Password { get; private set; } = string.Empty;

    public string? RankingUsername { get; private set; }

    public int? RankingPlayerId { get; private set; }

    private async void Save_Click(object sender, RoutedEventArgs e)
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
            RankingUsername = string.IsNullOrWhiteSpace(RankingUsernameBox.Text)
                ? null : RankingUsernameBox.Text.Trim();
            var effectiveUsername = RankingUsername ?? (Username.Length > 0 ? Username : null);
            RankingPlayerId = null;
            if (!string.IsNullOrWhiteSpace(PlayerProfileBox.Text))
            {
                var playerId = RankingIdentityResolver.ParsePlayerProfileReference(PlayerProfileBox.Text);
                if (playerId is null)
                {
                    ValidationText.Text = "Enter a positive player ID or a FourFold HTTPS player profile URL.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(effectiveUsername))
                {
                    ValidationText.Text = "Enter a ranking username to verify this player profile.";
                    return;
                }

                if (playerId == _initialPlayerId &&
                    string.Equals(effectiveUsername, _initialResolvedUsername, StringComparison.OrdinalIgnoreCase))
                {
                    RankingPlayerId = playerId;
                }
                else
                {
                    if (_verifyPlayer is null)
                    {
                        ValidationText.Text = "Player profile verification is unavailable.";
                        return;
                    }

                    SaveButton.IsEnabled = false;
                    ValidationText.Text = "Checking public player profile…";
                    try
                    {
                        if (!await _verifyPlayer(playerId.Value, effectiveUsername))
                        {
                            ValidationText.Text = "The profile name does not match that ranking username.";
                            return;
                        }
                    }
                    catch
                    {
                        ValidationText.Text = "Could not verify the player profile right now. Try again later.";
                        return;
                    }
                    finally
                    {
                        SaveButton.IsEnabled = true;
                    }

                    RankingPlayerId = playerId;
                }
            }

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
