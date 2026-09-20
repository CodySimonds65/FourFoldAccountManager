using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FourFoldWebViewProbe;

public partial class MainWindow : Window
{
    private static readonly Guid[] ProbeAccountIds =
    [
        Guid.Parse("11b9f3ef-b66d-4792-a62d-a82562b49d81"),
        Guid.Parse("b8f8342d-bf50-4c19-8bcb-f7e0fc51b5ab"),
        Guid.Parse("4f019a07-8c62-4a25-a5f2-93797aac9de8"),
        Guid.Parse("d3a03142-2ad6-4ea1-9b62-96098ab4680f")
    ];

    private static readonly Uri StartUri = new("https://fourfoldonline.com/play.php");
    private readonly (Grid Host, TextBlock Status)[] _slots;
    private string _statusLogPath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        _slots =
        [
            (HostOne, StatusOne),
            (HostTwo, StatusTwo),
            (HostThree, StatusThree),
            (HostFour, StatusFour)
        ];
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FourFoldAccountManager",
            "Probe");
        _statusLogPath = Path.Combine(root, "compatibility-status.txt");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(_statusLogPath, $"Started: {DateTimeOffset.UtcNow:O}{Environment.NewLine}");
            Log($"Executable: {Environment.ProcessPath ?? "<unknown>"}.");
            Log($"Probe data root is writable: {File.Exists(_statusLogPath)}.");
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: root);
            Log($"WebView2 environment initialized; effective user-data folder: {environment.UserDataFolder}.");

            for (var index = 0; index < _slots.Length; index++)
            {
                var profileNumber = index + 1;
                var slot = _slots[index];
                slot.Status.Text = $"Profile {profileNumber}: initializing…";
                try
                {
                    var options = environment.CreateCoreWebView2ControllerOptions();
                    options.ProfileName = ProbeAccountIds[index].ToString("N");
                    options.IsInPrivateModeEnabled = false;

                    var browser = new WebView2();
                    slot.Host.Children.Add(browser);
                    await browser.EnsureCoreWebView2Async(environment, options);
                    browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                    var profile = browser.CoreWebView2.Profile;
                    Log($"Profile {profileNumber}: initialized; runtime name={profile.ProfileName}; inPrivate={profile.IsInPrivateModeEnabled}.");

                    browser.CoreWebView2.NavigationStarting += (_, args) =>
                    {
                        Log($"Profile {profileNumber}: navigation started to {GetOriginForLog(args.Uri)}.");
                        if (!IsFourFoldUri(args.Uri))
                        {
                            args.Cancel = true;
                            slot.Status.Text = "Blocked navigation outside FourFold Online.";
                            Log($"Profile {profileNumber}: blocked unapproved top-level navigation.");
                            return;
                        }

                        slot.Status.Text = $"Profile {profileNumber}: loading FourFold Online…";
                    };

                    browser.CoreWebView2.NavigationCompleted += (_, args) =>
                    {
                        slot.Status.Text = args.IsSuccess
                            ? $"Profile {profileNumber}: page loaded. Sign in manually here."
                            : $"Profile {profileNumber}: page navigation failed; retry in this slot.";
                        Log($"Profile {profileNumber}: navigation completed; success={args.IsSuccess}; webError={args.WebErrorStatus}.");
                    };

                    browser.CoreWebView2.NewWindowRequested += (_, args) =>
                    {
                        args.Handled = true;
                        Log($"Profile {profileNumber}: popup requested {GetOriginForLog(args.Uri)}.");
                        if (IsFourFoldUri(args.Uri))
                        {
                            browser.CoreWebView2.Navigate(args.Uri);
                            slot.Status.Text = $"Profile {profileNumber}: opened the FourFold page in this profile.";
                        }
                        else
                        {
                            slot.Status.Text = "Blocked a popup outside FourFold Online.";
                        }
                    };

                    browser.CoreWebView2.ProcessFailed += (_, args) =>
                    {
                        slot.Status.Text = $"Profile {profileNumber}: browser process failed; other slots remain available.";
                        Log($"Profile {profileNumber}: WebView2 process failed; kind={args.ProcessFailedKind}.");
                    };

                    browser.CoreWebView2.Navigate(StartUri.AbsoluteUri);
                }
                catch (Exception exception)
                {
                    slot.Status.Text = $"Profile {profileNumber}: {exception.GetType().Name}.";
                    Log($"Profile {profileNumber}: initialization failed; exception={exception.GetType().Name}.");
                }
            }
        }
        catch (Exception exception)
        {
            foreach (var slot in _slots)
            {
                slot.Status.Text = $"WebView2 unavailable: {exception.GetType().Name}.";
            }
            Log($"Probe startup failed; exception={exception.GetType().Name}.");
        }
    }

    private void Log(string value)
    {
        if (!string.IsNullOrEmpty(_statusLogPath))
        {
            File.AppendAllText(_statusLogPath, $"{DateTimeOffset.UtcNow:O} {value}{Environment.NewLine}");
        }
    }

    private static string GetOriginForLog(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.IdnHost}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}"
            : "<invalid-uri>";

    private static bool IsFourFoldUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, StartUri.Host, StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(uri.UserInfo);
}
