using System.IO;
using System.Net;
using System.Net.Http;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Services;

public sealed class FourFoldRankingClient : IDisposable
{
    private const int MaximumPageBytes = 2 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly TimeSpan _requestTimeout;

    public FourFoldRankingClient() : this(CreateHttpClient(), TimeSpan.FromSeconds(15)) { }

    public FourFoldRankingClient(HttpClient http, TimeSpan requestTimeout)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        _requestTimeout = requestTimeout;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://fourfoldonline.com/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FourFoldAccountManager/1.1 XPTracker");
        return http;
    }

    public async Task<IReadOnlyList<RankingEntry>> GetRankingAsync(CancellationToken cancellationToken) =>
        RankingHtmlParser.Parse(await GetPageAsync("ranking.php?mode=total_exp&limit=200", cancellationToken));

    public async Task<PlayerProgressSnapshot> GetProfileAsync(int playerId, CancellationToken cancellationToken)
    {
        if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
        return PlayerProfileHtmlParser.Parse(await GetPageAsync($"player.php?id={playerId}", cancellationToken));
    }

    private async Task<string> GetPageAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        var requestToken = deadline.Token;
        using var response = await _http.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, requestToken);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("FourFold redirected the public data request.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumPageBytes)
            throw new InvalidDataException("The FourFold page exceeds the tracker size limit.");

        await using var source = await response.Content.ReadAsStreamAsync(requestToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, requestToken)) > 0)
        {
            if (destination.Length + count > MaximumPageBytes)
                throw new InvalidDataException("The FourFold page exceeds the tracker size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, count), requestToken);
        }

        return System.Text.Encoding.UTF8.GetString(destination.ToArray());
    }

    public void Dispose() => _http.Dispose();
}
