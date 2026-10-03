using System.Net;
using FourFoldAccountManager.Core.Plugins.Hub;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class HubHttpTests
{
    // No Content-Length, so only counting the bytes as they arrive can stop an oversized download.
    private sealed class UnknownLengthContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class FixedHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(body),
                RequestMessage = request
            });
    }

    [Fact]
    public async Task ADownloadLargerThanTheLimitIsRefused()
    {
        using var atLimit = new HttpClient(new FixedHandler(new byte[100]));
        using var overLimit = new HttpClient(new FixedHandler(new byte[101]));
        var uri = new Uri("https://example.com/file");

        Assert.Equal(100, (await HubHttp.DownloadAsync(atLimit, uri, 100, CancellationToken.None))!.Length);
        Assert.Null(await HubHttp.DownloadAsync(overLimit, uri, 100, CancellationToken.None));
    }
}
