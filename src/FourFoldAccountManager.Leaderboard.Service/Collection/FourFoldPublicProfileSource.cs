using System.Net;
using System.Text;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Leaderboard.Service.Collection;

public sealed class FourFoldPublicProfileSource : ILeaderboardPublicProfileSource, IDisposable
{
    private const int MaximumPageBytes = 2 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly LeaderboardCollectionOptions _options;

    public FourFoldPublicProfileSource(LeaderboardCollectionOptions options, HttpMessageHandler? handler = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        handler ??= new HttpClientHandler { AllowAutoRedirect = false };
        if (handler is HttpClientHandler httpHandler) httpHandler.AllowAutoRedirect = false;
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = MaximumPageBytes
        };
    }

    public async Task<PlayerProgressSnapshot> FetchAsync(int playerId, CancellationToken ct)
    {
        if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
        if (!_options.CanCollect)
            throw new InvalidOperationException("Leaderboard collection is disabled or has no approved interval.");
        var template = _options.ProfileUrlTemplate;
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{playerId}", StringComparison.Ordinal))
            throw new InvalidOperationException("An approved profile URL template is required.");
        var url = template.Replace("{playerId}", playerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The approved profile URL must use HTTPS.");

        // The body is buffered up to MaxResponseContentBufferSize; a larger page throws HttpRequestException.
        using var response = await _http.GetAsync(uri, ct);
        if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest ||
            response.RequestMessage?.RequestUri is { } finalUri && finalUri != uri)
            throw new HttpRequestException("The public profile request redirected.");
        response.EnsureSuccessStatusCode();
        return PlayerProfileHtmlParser.Parse(Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(ct)));
    }

    public void Dispose() => _http.Dispose();
}
