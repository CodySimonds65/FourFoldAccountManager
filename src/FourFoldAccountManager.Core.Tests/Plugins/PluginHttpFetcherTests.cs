using System.Net;
using FourFoldAccountManager.Core.Plugins;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class PluginHttpFetcherTests
{
    private static readonly PluginManifest Manifest = new(
        "cody.goal-tracker", "Goal tracker", "Goals", "1.0.0", "Cody", "", 1, "index.html", null,
        [new Uri("https://wiki.example.com")], false, []);

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
                ["Host"] = "attacker.example.net",
                ["Sec-Fetch-Site"] = "same-origin",
                ["Proxy-Authorization"] = "Basic x",
                ["X-Api-Key"] = "abc"
            }, null));

        Assert.NotNull(sent);
        Assert.False(sent.Headers.Contains("Cookie"));
        Assert.False(sent.Headers.Contains("Sec-Fetch-Site"));
        Assert.False(sent.Headers.Contains("Proxy-Authorization"));
        Assert.Null(sent.Headers.Host);
        Assert.Equal("abc", sent.Headers.GetValues("X-Api-Key").Single());
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

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
