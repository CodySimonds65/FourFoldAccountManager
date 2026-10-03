using System.Net;
using FourFoldAccountManager.Core.Plugins;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class PluginHttpFetcherTests
{
    private static readonly PluginManifest Manifest = new(
        "cody.goal-tracker", "Goal tracker", "Goals", "1.0.0", "Cody", "", 1, "index.html", null,
        [new Uri("https://wiki.example.com")], false, []);

    private static readonly PluginManifest MultiSiteManifest = new(
        "cody.goal-tracker", "Goal tracker", "Goals", "1.0.0", "Cody", "", 1, "index.html", null,
        [new Uri("https://wiki.example.com"), new Uri("https://cdn.example.net")], false, []);

    private static PluginHttpRequest Get(string url) => new(url, "GET", null, null);

    [Fact]
    public async Task AnUndeclaredSiteIsRefusedWithoutSendingAnything()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://attacker.example.net/steal")));

        Assert.Equal("site-not-allowed", error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ARedirectToAnUndeclaredSiteIsRefusedAndOneToADeclaredSiteIsFollowed()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/out" => Redirect("https://attacker.example.net/"),
            "/in" => Redirect("/page"),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("page text") }
        });
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/out")));
        var followed = await fetcher.FetchAsync(Get("https://wiki.example.com/in"));

        Assert.Equal("site-not-allowed", error.Code);
        Assert.DoesNotContain(handler.Requests, uri => uri.Host == "attacker.example.net");
        Assert.Equal((200, "page text"), (followed.Status, followed.Text));
    }

    [Fact]
    public async Task MoreThanFiveRedirectsAreRefused()
    {
        var handler = new FakeHandler(_ => Redirect("/again"));
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/loop")));

        Assert.Equal("limit-exceeded", error.Code);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task ForbiddenHeadersAreDroppedAndOthersAreSent()
    {
        HttpRequestMessage? sent = null;
        var handler = new FakeHandler(request =>
        {
            sent = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        await fetcher.FetchAsync(new PluginHttpRequest("https://wiki.example.com/api", "GET",
            new Dictionary<string, string>
            {
                ["Cookie"] = "session=1",
                ["cookie"] = "lowercase=1",
                ["Host"] = "attacker.example.net",
                ["Sec-Fetch-Site"] = "same-origin",
                ["SEC-Fetch-Mode"] = "cors",
                ["Proxy-Authorization"] = "Basic x",
                ["proxy-connection"] = "keep-alive",
                ["Connection"] = "close",
                ["X-Keep"] = "yes"
            }, null));

        Assert.NotNull(sent);
        Assert.False(sent.Headers.Contains("Cookie"));
        Assert.False(sent.Headers.Contains("cookie"));
        Assert.False(sent.Headers.Contains("Sec-Fetch-Site"));
        Assert.False(sent.Headers.Contains("SEC-Fetch-Mode"));
        Assert.False(sent.Headers.Contains("Proxy-Authorization"));
        Assert.False(sent.Headers.Contains("proxy-connection"));
        Assert.False(sent.Headers.Contains("Connection"));
        Assert.Null(sent.Headers.Host);
        Assert.Null(sent.Content);
        Assert.Equal("yes", sent.Headers.GetValues("X-Keep").Single());
    }

    [Fact]
    public async Task AResponseOverTwoMegabytesIsRefused()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[PluginHttpFetcher.MaximumResponseBytes + 1])
        });
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/big")));

        Assert.Equal("limit-exceeded", error.Code);
    }

    [Fact]
    public async Task AStreamedResponseOverTwoMegabytesIsRefused()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[PluginHttpFetcher.MaximumResponseBytes + 1])
        });
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/big")));

        Assert.Equal("limit-exceeded", error.Code);
    }

    [Fact]
    public async Task TheSixtyFirstRequestInAMinuteIsRefusedAndAllowedAgainLater()
    {
        var clock = new FakeClock();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler, clock);
        for (var count = 0; count < 60; count++)
        {
            await fetcher.FetchAsync(Get("https://wiki.example.com/"));
        }

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/")));
        clock.Advance(TimeSpan.FromSeconds(61));
        await fetcher.FetchAsync(Get("https://wiki.example.com/"));

        Assert.Equal("limit-exceeded", error.Code);
        Assert.Equal(61, handler.Requests.Count);
    }

    [Fact]
    public async Task OnlyGetAndPostAreAccepted()
    {
        using var fetcher = new PluginHttpFetcher(
            Manifest, PluginTrust.Developer, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(new PluginHttpRequest("https://wiki.example.com/", "DELETE", null, null)));

        Assert.Equal("invalid-argument", error.Code);
    }

    [Theory]
    [InlineData("192.168.1.5")]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8,10.0.0.7")]
    public async Task AHostThatResolvesToTheLocalNetworkIsRefused(string addresses)
    {
        var resolved = addresses.Split(',').Select(IPAddress.Parse).ToArray();

        var error = await Assert.ThrowsAsync<PluginApiException>(() =>
            PluginHttpFetcher.ResolvePublicAddressesAsync(
                "wiki.example.com", (_, _) => Task.FromResult(resolved), CancellationToken.None));

        Assert.Equal("site-not-allowed", error.Code);
    }

    [Fact]
    public async Task AHostThatResolvesToPublicAddressesReturnsThemAll()
    {
        var expected = new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1") };
        var result = await PluginHttpFetcher.ResolvePublicAddressesAsync(
            "wiki.example.com", (_, _) => Task.FromResult(expected), CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("v\r\nCookie: x")]
    [InlineData("v\nHost: evil")]
    [InlineData("v\rX: y")]
    [InlineData("café")]
    public async Task AHeaderValueWithALineBreakIsRefusedAndNothingIsSent(string invalidValue)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(new PluginHttpRequest("https://wiki.example.com/", "GET",
                new Dictionary<string, string> { ["X-Test"] = invalidValue }, null)));

        Assert.Equal("invalid-argument", error.Code);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("X:Y")]
    [InlineData("")]
    [InlineData("Cookie ")]
    public async Task AnInvalidHeaderNameIsRefused(string invalidName)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(new PluginHttpRequest("https://wiki.example.com/", "POST",
                new Dictionary<string, string> { [invalidName] = "value" }, "body")));

        Assert.Equal("invalid-argument", error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ARequestThatTakesTooLongIsRefused()
    {
        var handler = new HangingHandler();
        using var fetcher = new PluginHttpFetcher(
            Manifest, PluginTrust.Developer, handler, timeout: TimeSpan.FromMilliseconds(100));

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/")));

        Assert.Equal("unavailable", error.Code);
    }

    [Fact]
    public async Task AMalformedRedirectIsRefusedCleanly()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.TryAddWithoutValidation("Location", "//host:99999/");
            return response;
        });
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/")));

        Assert.Equal("unavailable", error.Code);
    }

    [Fact]
    public async Task ABrokenBodyIsRefusedCleanly()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingContent()
        });
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/")));

        Assert.Equal("unavailable", error.Code);
    }

    [Fact]
    public async Task PluginHeadersAreNotForwardedToAnotherHost()
    {
        var capturedHeaders = new List<(Uri, bool hasAuth)>();
        var handler = new FakeHandler(request =>
        {
            var hasAuth = request.Headers.Contains("Authorization");
            capturedHeaders.Add((request.RequestUri!, hasAuth));

            if (request.RequestUri!.Host == "wiki.example.com")
            {
                return Redirect("https://cdn.example.net/x");
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        using var fetcher = new PluginHttpFetcher(MultiSiteManifest, PluginTrust.Developer, handler);

        await fetcher.FetchAsync(new PluginHttpRequest("https://wiki.example.com/", "GET",
            new Dictionary<string, string> { ["Authorization"] = "Bearer t" }, null));

        Assert.Equal(2, capturedHeaders.Count);
        Assert.True(capturedHeaders[0].hasAuth, "First request should have Authorization");
        Assert.False(capturedHeaders[1].hasAuth, "Second request to different host should not have Authorization");
    }

    [Fact]
    public async Task RedirectHopsCountTowardsTheRateLimit()
    {
        var clock = new FakeClock();
        var requestCount = 0;
        var handler = new FakeHandler(_ =>
        {
            requestCount++;
            // Each fetch can have up to 5 redirects (6 total requests), so 10 fetches = 60 requests
            var withinFetch = requestCount % 6;
            if (withinFetch != 0)
            {
                return Redirect("/again");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler, clock);

        // Make 10 successful fetches (each with 6 requests = 60 total)
        for (var i = 0; i < 10; i++)
        {
            await fetcher.FetchAsync(Get("https://wiki.example.com/start"));
        }

        // The 11th fetch should hit the rate limit
        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/start")));

        Assert.Equal("limit-exceeded", error.Code);
        Assert.Equal(60, requestCount);
    }

    [Fact]
    public async Task PluginHeadersAreNotSentAgainAfterReturningToTheOriginalHost()
    {
        var capturedHeaders = new List<(Uri, bool hasAuth)>();
        var handler = new FakeHandler(request =>
        {
            var hasAuth = request.Headers.Contains("Authorization");
            capturedHeaders.Add((request.RequestUri!, hasAuth));

            if (request.RequestUri!.Host == "wiki.example.com" && request.RequestUri.AbsolutePath == "/start")
            {
                return Redirect("https://cdn.example.net/x");
            }

            if (request.RequestUri!.Host == "cdn.example.net")
            {
                return Redirect("https://wiki.example.com/back");
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        using var fetcher = new PluginHttpFetcher(MultiSiteManifest, PluginTrust.Developer, handler);

        await fetcher.FetchAsync(new PluginHttpRequest("https://wiki.example.com/start", "GET",
            new Dictionary<string, string> { ["Authorization"] = "Bearer t" }, null));

        Assert.Equal(3, capturedHeaders.Count);
        Assert.True(capturedHeaders[0].hasAuth, "First request (wiki) should have Authorization");
        Assert.False(capturedHeaders[1].hasAuth, "Second request (cdn) should not have Authorization");
        Assert.False(capturedHeaders[2].hasAuth, "Third request (back to wiki) should not have Authorization");
    }

    [Fact]
    public async Task PluginHeadersAreNotSentToAnotherPortOnTheSameHost()
    {
        var manifest = MultiSiteManifest with
        {
            Sites = [new Uri("https://wiki.example.com"), new Uri("https://wiki.example.com:8443")]
        };
        var sawAuth = new List<bool>();
        var handler = new FakeHandler(request =>
        {
            sawAuth.Add(request.Headers.Contains("Authorization"));
            return request.RequestUri!.Port == 443
                ? Redirect("https://wiki.example.com:8443/x")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        using var fetcher = new PluginHttpFetcher(manifest, PluginTrust.Developer, handler);

        await fetcher.FetchAsync(new PluginHttpRequest("https://wiki.example.com/start", "GET",
            new Dictionary<string, string> { ["Authorization"] = "Bearer t" }, null));

        Assert.Equal([true, false], sawAuth);
    }

    [Fact]
    public async Task FramingHeadersFromThePluginAreIgnoredOnAPost()
    {
        HttpRequestMessage? sent = null;
        long? contentLength = null;
        var handler = new FakeHandler(request =>
        {
            sent = request;
            contentLength = sent.Content?.Headers.ContentLength;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler);

        await fetcher.FetchAsync(new PluginHttpRequest("https://wiki.example.com/", "POST",
            new Dictionary<string, string>
            {
                ["Content-Length"] = "999",
                ["Transfer-Encoding"] = "chunked",
                ["Connection"] = "close",
                ["X-Keep"] = "1"
            }, "hello"));

        Assert.NotNull(sent);
        Assert.Equal(5, contentLength);
        Assert.True(sent.Headers.TransferEncodingChunked != true);
        Assert.False(sent.Headers.Contains("Transfer-Encoding"));
        Assert.False(sent.Headers.Contains("Connection"));
        Assert.Equal("1", sent.Headers.GetValues("X-Keep").Single());
    }

    [Fact]
    public async Task AnInvalidRequestDoesNotUseTheRateLimit()
    {
        var clock = new FakeClock();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fetcher = new PluginHttpFetcher(Manifest, PluginTrust.Developer, handler, clock);

        // Make 60 invalid requests (invalid header values) — none should consume rate slots
        for (var i = 0; i < 60; i++)
        {
            await Assert.ThrowsAsync<PluginApiException>(
                () => fetcher.FetchAsync(new PluginHttpRequest("https://wiki.example.com/", "GET",
                    new Dictionary<string, string> { ["X-Test"] = "v\r\nCookie: x" }, null)));
        }

        // Now make 60 valid requests — all should succeed
        for (var i = 0; i < 60; i++)
        {
            await fetcher.FetchAsync(Get("https://wiki.example.com/"));
        }

        // The 61st valid request should hit the rate limit
        var error = await Assert.ThrowsAsync<PluginApiException>(
            () => fetcher.FetchAsync(Get("https://wiki.example.com/")));

        Assert.Equal("limit-exceeded", error.Code);
    }

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class UnknownLengthContent(byte[] data) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(data);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw new InvalidDataException("broken");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
