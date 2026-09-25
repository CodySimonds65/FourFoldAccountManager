using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FourFoldAccountManager.Desktop.Tests;

// WPF allows one Application per process and ties its resources to one thread,
// so every UI test runs on this shared STA dispatcher with the app's resources loaded.
internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(StartDispatcher);

    public static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ExceptionDispatchInfo? failure = null;
        SharedDispatcher.Value.Invoke(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        failure?.Throw();
    }

    private static Dispatcher StartDispatcher()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var resource in new[] { "Resources/Theme.xaml", "Resources/OverlayCardTemplates.xaml" })
            {
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/FourFoldAccountManager.Desktop;component/{resource}", UriKind.Relative)
                });
            }

            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
