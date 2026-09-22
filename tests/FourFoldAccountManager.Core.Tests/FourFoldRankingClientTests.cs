using System.Net;
using FourFoldAccountManager.Desktop.Services;

namespace FourFoldAccountManager.Core.Tests;

public class FourFoldRankingClientTests
{
    [Fact]
    public async Task Body_stall_is_bounded_by_request_deadline()
    {
        using var http = new HttpClient(new StallingHandler())
        {
            BaseAddress = new Uri("https://fourfoldonline.com/")
        };
        using var client = new FourFoldRankingClient(http, TimeSpan.FromMilliseconds(50));
        var request = client.GetRankingAsync(CancellationToken.None);
        var completed = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(request, completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    private sealed class StallingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StallingStream())
            });
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            AwaitCancellation(cancellationToken);
        private static async ValueTask<int> AwaitCancellation(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
