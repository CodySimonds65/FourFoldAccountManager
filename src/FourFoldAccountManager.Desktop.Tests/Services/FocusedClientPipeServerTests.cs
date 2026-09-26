using System.IO;
using System.IO.Pipes;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class FocusedClientPipeServerTests
{
    [Fact]
    public void PipeReturnsSnapshotFromDispatcher()
    {
        var dispatcher = WpfTestHost.StartDispatcher(() => { });
        using var server = new FocusedClientPipeServer(dispatcher, () =>
        {
            Assert.True(dispatcher.CheckAccess());
            return "{\"slot\":1,\"rect\":[10,20,300,400]}";
        });

        using var client = new NamedPipeClientStream(
            ".", $"FourFoldFocusedClient-{Environment.ProcessId}", PipeDirection.In);
        client.Connect(3000);
        using var reader = new StreamReader(client);
        Assert.Equal("{\"slot\":1,\"rect\":[10,20,300,400]}", reader.ReadLine());
    }
}
