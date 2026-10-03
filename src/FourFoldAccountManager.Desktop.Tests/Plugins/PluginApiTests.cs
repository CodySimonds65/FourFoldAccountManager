using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Desktop.Plugins.Web;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Plugins;

public sealed class PluginApiTests : IDisposable
{
    private const string PluginId = "cody.goal-tracker";

    private static readonly Guid KnownAccount = Guid.Parse("6b1d7c3e-2f4a-4c58-9e0b-1a2b3c4d5e6f");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fourfold-plugin-api-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHost _host = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly PluginCardStore _cards = new();
    private readonly PluginManifest _manifest;
    private readonly PluginHttpFetcher _http;
    private readonly PluginApi _api;

    public PluginApiTests()
    {
        _manifest = new PluginManifest(
            PluginId, "Goal tracker", "Goals", "1.0.0", "Cody", "Tracks goals.", 1, "index.html", null,
            [new Uri("https://example.com")], false,
            [
                new PluginCardManifest("goal", "Goal", OverlayAddOnScope.Account),
                new PluginCardManifest("summary", "Summary", OverlayAddOnScope.Global)
            ]) { Folder = _folder };
        _host.Accounts = [new PluginAccountInfo(KnownAccount, "Main", "Cody", true)];
        _http = new PluginHttpFetcher(_manifest, PluginTrust.Standard);
        _api = new PluginApi(
            _manifest, PluginTrust.Standard, _host, new PluginStorage(Path.Combine(_folder, "storage.json")), _http,
            _cards, _clock);
    }

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task OpenExternalOnlyOpensPublicHttpsLinksOnDeclaredSites()
    {
        _host.PanelShowing = true;

        foreach (var url in new[]
                 {
                     "file:///C:/Windows/System32/calc.exe", "javascript:alert(1)", "http://example.com/",
                     "https://localhost/", "https://192.168.1.1/", "https://printer.local/",
                     "https://user@example.com/", "ms-settings:",
                     "not a url"
                 })
        {
            Assert.Equal("invalid-argument", ErrorCode(await Call("openExternal", new { url })));
        }

        // The plugin declares example.com and nothing else. Its own address, and another plugin's, are never sites.
        foreach (var url in new[]
                 {
                     "https://example.org/", "https://sub.example.com/", "https://example.com:8443/",
                     "https://cody--goal-tracker.fourfoldplugin/", "https://other--plugin.fourfoldplugin/"
                 })
        {
            Assert.Equal("site-not-allowed", ErrorCode(await Call("openExternal", new { url })));
        }

        Assert.Empty(_host.Opened);

        var opened = await Call("openExternal", new { url = "https://example.com/page" });

        Assert.Null(ErrorCode(opened));
        Assert.Equal(new Uri("https://example.com/page"), Assert.Single(_host.Opened));
    }

    [Fact]
    public async Task OnlyAPluginClearedForAnyWebsiteCanOpenLinksAnywhere()
    {
        _host.PanelShowing = true;
        var asking = _manifest with { AnySite = true };
        var notCleared = new PluginApi(
            asking, PluginTrust.Standard, _host, new PluginStorage(Path.Combine(_folder, "a.json")), _http, _cards, _clock);
        var cleared = new PluginApi(
            asking, PluginTrust.Verified, _host, new PluginStorage(Path.Combine(_folder, "b.json")), _http, _cards, _clock);
        var link = new { url = "https://example.org/" };

        // Asking for any website in plugin.json isn't enough; the hub must have cleared it.
        Assert.Equal("site-not-allowed", ErrorCode(await Call(notCleared, "openExternal", link)));
        Assert.Empty(_host.Opened);

        Assert.Null(ErrorCode(await Call(cleared, "openExternal", link)));
        Assert.Equal(new Uri("https://example.org/"), Assert.Single(_host.Opened));
        Assert.Equal("invalid-argument", ErrorCode(await Call(cleared, "openExternal", new { url = "https://localhost/" })));
    }

    [Fact]
    public async Task OpenExternalNeedsTheShowingPanelAndIsRateLimited()
    {
        var link = new { url = "https://example.com/" };

        Assert.Equal("unavailable", ErrorCode(await Call("openExternal", link)));
        Assert.Empty(_host.Opened);

        _host.PanelShowing = true;
        Assert.Null(ErrorCode(await Call("openExternal", link)));
        Assert.Single(_host.Opened);

        Assert.Equal("limit-exceeded", ErrorCode(await Call("openExternal", link)));
        Assert.Single(_host.Opened);

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(ErrorCode(await Call("openExternal", link)));
        Assert.Equal(2, _host.Opened.Count);
    }

    [Fact]
    public async Task ACardCanOnlyBeSetForADeclaredCardAndAKnownAccount()
    {
        Assert.Equal(
            "not-declared", ErrorCode(await Call("cards.set", new { cardId = "other", accountId = KnownAccount })));
        Assert.Equal(
            "invalid-argument", ErrorCode(await Call("cards.set", new { cardId = "goal", accountId = Guid.NewGuid() })));

        // A right-to-left override (U+202E) in a label would reverse the text drawn after it.
        Assert.Equal(
            "invalid-argument",
            ErrorCode(await Call("cards.set", new
            {
                cardId = "goal",
                accountId = KnownAccount,
                rows = new[] { new { label = "Done\u202E", value = "3" } }
            })));

        var key = new OverlayCardKey(OverlayAddOnKind.Plugin, KnownAccount, "cody.goal-tracker/goal");
        Assert.Null(_cards.Get(key));

        var reply = await Call("cards.set", new
        {
            cardId = "goal",
            accountId = KnownAccount,
            summary = "3 of 5",
            rows = new[] { new { label = "Done", value = "3", progress = 0.6 } }
        });

        Assert.Null(ErrorCode(reply));
        var content = Assert.IsType<PluginCardContent>(_cards.Get(key));
        Assert.Equal("3 of 5", content.Summary);
        var row = Assert.Single(content.Rows);
        Assert.Equal(("Done", "3", 0.6), (row.Label, row.Value, row.Progress));
    }

    [Fact]
    public async Task HostileMessagesNeverThrow()
    {
        var messages = new[]
        {
            "not json",
            "[]",
            "{}",
            """{"id":"x"}""",
            """{"id":1}""",
            """{"id":1,"method":5}""",
            """{"id":1,"method":"xp.get","params":[]}""",
            """{"id":1,"method":"cards.set","params":{"cardId":"goal","rows":"x"}}""",
            """{"id":1,"method":"cards.set","params":{"cardId":"summary","rows":[5,null,{"progress":"x"}]}}"""
        };

        foreach (var message in messages)
        {
            var reply = await _api.HandleAsync(message);

            if (reply is null)
            {
                continue;
            }

            using var document = JsonDocument.Parse(reply);
            Assert.True(
                document.RootElement.TryGetProperty("result", out _) || document.RootElement.TryGetProperty("error", out _),
                $"The reply to {message[..Math.Min(message.Length, 60)]} had neither a result nor an error.");
        }

        var tooLong = "{\"id\":1,\"method\":\"storage.set\",\"params\":{\"key\":\"k\",\"value\":\"" +
                      new string('x', PluginApi.MaximumMessageLength) + "\"}}";
        Assert.Null(await _api.HandleAsync(tooLong));
    }

    [Fact]
    public void CallsBeyondTheInFlightLimitAreRefusedNotQueued()
    {
        // Resumed work waits in a queue until the test runs it, so the calls stay in flight the way a slow disk keeps
        // them (the app resumes them on the UI thread one at a time).
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var calls = Enumerable.Range(1, 100)
                .Select(id => _api.HandleAsync(JsonSerializer.Serialize(
                    new { id, method = "storage.set", @params = new { key = "k" + id, value = "v" } })))
                .ToList();
            context.RunUntil(Task.WhenAll(calls));

            var codes = calls.Select(call => ErrorCode(JsonDocument.Parse(call.Result!).RootElement)).ToList();
            Assert.Contains("limit-exceeded", codes);
            Assert.True(codes.Count(code => code is null) <= PluginApi.MaximumCallsInFlight);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task StorageWritesAreRateLimited()
    {
        for (var write = 0; write < PluginApi.MaximumStorageWritesPerMinute; write++)
        {
            var reply = write % 2 == 0
                ? await Call("storage.set", new { key = "k", value = write })
                : await Call("storage.remove", new { key = "k" });
            Assert.Null(ErrorCode(reply));
        }

        Assert.Equal("limit-exceeded", ErrorCode(await Call("storage.set", new { key = "k", value = 0 })));

        _clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Null(ErrorCode(await Call("storage.set", new { key = "k", value = 1 })));
    }

    private Task<JsonElement> Call(string method, object parameters) => Call(_api, method, parameters);

    private static async Task<JsonElement> Call(PluginApi api, string method, object parameters)
    {
        var reply = await api.HandleAsync(JsonSerializer.Serialize(new { id = 1, method, @params = parameters }));
        Assert.NotNull(reply);
        return JsonDocument.Parse(reply).RootElement;
    }

    private static string? ErrorCode(JsonElement reply) =>
        reply.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));

        public void RunUntil(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted)
            {
                Assert.True(DateTime.UtcNow < deadline, "The calls never finished.");
                if (_queue.TryTake(out var item, 50))
                {
                    item.Callback(item.State);
                }
            }
        }
    }

    private sealed class FakeHost : IPluginHostData
    {
        public IReadOnlyList<PluginAccountInfo> Accounts { get; set; } = [];

        public bool PanelShowing { get; set; }

        public List<Uri> Opened { get; } = [];

        public IReadOnlyList<PluginAccountInfo> GetAccounts() => Accounts;

        public PluginXpInfo? GetXp(Guid accountId) => null;

        public PluginStatsInfo? GetStats(Guid accountId) => null;

        public PluginTimerInfo GetTimer() => new("stopped", 0, []);

        public bool IsPanelShowing(string pluginId) => PanelShowing;

        public void OpenInBrowser(Uri uri) => Opened.Add(uri);
    }
}
