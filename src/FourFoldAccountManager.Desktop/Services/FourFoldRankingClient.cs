using System.IO;
using System.Net;
using System.Net.Http;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Services;

public sealed class FourFoldRankingClient : IDisposable
{
    private const int MaximumPageBytes = 2 * 1024 * 1024;
    private readonly HttpClient _http;

    public FourFoldRankingClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://fourfoldonline.com/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FourFoldAccountManager/1.1 XPTracker");
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
        using var response = await _http.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("FourFold redirected the public data request.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumPageBytes)
            throw new InvalidDataException("The FourFold page exceeds the tracker size limit.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (destination.Length + count > MaximumPageBytes)
                throw new InvalidDataException("The FourFold page exceeds the tracker size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        return System.Text.Encoding.UTF8.GetString(destination.ToArray());
    }

    public void Dispose() => _http.Dispose();
}
