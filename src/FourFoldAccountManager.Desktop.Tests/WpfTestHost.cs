using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FourFoldAccountManager.Desktop.Tests;

// WPF allows one Application per process and ties its resources to one thread,
// so every UI test runs on this shared STA dispatcher with the app's resources loaded.
internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(() => StartDispatcher(InitializeApplication));

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

    private static void InitializeApplication()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var resource in new[] { "Resources/Theme.xaml", "Resources/OverlayCardTemplates.xaml" })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/FourFoldAccountManager.Desktop;component/{resource}", UriKind.Relative)
            });
        }
    }

    // Internal (rather than private) so a test can exercise the failure path with a stand-in
    // initializer, without ever letting an exception on the dispatcher thread go unhandled and
    // take down the whole test host process.
    internal static Dispatcher StartDispatcher(Action initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        ExceptionDispatchInfo? startupFailure = null;
        var thread = new Thread(() =>
        {
            try
            {
                initialize();
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                startupFailure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                ready.Set();
            }

            if (startupFailure is null)
            {
                Dispatcher.Run();
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        if (startupFailure is not null)
        {
            throw new InvalidOperationException(
                "The shared WPF test dispatcher failed to start. See the inner exception for the original failure.",
                startupFailure.SourceException);
        }

        return dispatcher!;
    }
}
