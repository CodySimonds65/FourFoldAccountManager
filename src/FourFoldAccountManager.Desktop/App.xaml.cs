using System.Windows;
using FourFoldAccountManager.Desktop.Updates;

namespace FourFoldAccountManager.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (UpdateInstaller.TryRunReplacementMode(e.Args))
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
