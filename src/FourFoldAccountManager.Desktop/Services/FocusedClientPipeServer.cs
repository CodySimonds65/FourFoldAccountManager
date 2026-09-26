using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Threading;

namespace FourFoldAccountManager.Desktop.Services;

/// <summary>Reports the focused game view to local tools without accepting input commands.</summary>
internal sealed class FocusedClientPipeServer : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<string> _snapshot;
    private readonly CancellationTokenSource _stop = new();

    public FocusedClientPipeServer(Dispatcher dispatcher, Func<string> snapshot)
    {
        _dispatcher = dispatcher;
        _snapshot = snapshot;
        _ = Task.Run(ServeAsync);
    }

    private async Task ServeAsync()
    {
        var pipeName = $"FourFoldFocusedClient-{Environment.ProcessId}";
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                var payload = await _dispatcher.InvokeAsync(
                    _snapshot, DispatcherPriority.Send, _stop.Token);
                await using var writer = new StreamWriter(pipe, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
                await writer.WriteLineAsync(payload);
                await writer.FlushAsync(_stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A disconnected client may try again with the next pipe instance.
            }
        }
    }

    public void Dispose() => _stop.Cancel();
}
