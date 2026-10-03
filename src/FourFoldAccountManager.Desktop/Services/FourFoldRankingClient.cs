using System.Net;
using System.Net.Http;
using System.Text;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Services;

public sealed class FourFoldRankingClient : IDisposable, IPlayerProfileTransport
{
    private const int MaximumPageBytes = 2 * 1024 * 1024;
    private readonly HttpClient _http = CreateHttpClient();

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
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = MaximumPageBytes
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

    // Redirects are off, so a 3xx fails the success check. A page over MaxResponseContentBufferSize fails the
    // same way, with an HttpRequestException.
    private async Task<string> GetPageAsync(string relativePath, CancellationToken cancellationToken) =>
        Encoding.UTF8.GetString(await _http.GetByteArrayAsync(relativePath, cancellationToken));

    public void Dispose() => _http.Dispose();
}
