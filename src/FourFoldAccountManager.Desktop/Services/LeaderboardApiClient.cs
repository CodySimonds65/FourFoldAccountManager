using System.Net.Http;
using System.IO;
using System.Net.Http.Json;
using System.Text.Json;
using FourFoldAccountManager.Core.Leaderboard;

namespace FourFoldAccountManager.Desktop.Services;

public sealed class LeaderboardApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly bool _ownsClient;

    public LeaderboardApiClient(HttpClient httpClient, LeaderboardApiOptions options, bool ownsClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseAddress = options?.BaseAddress ?? throw new ArgumentNullException(nameof(options));
        if (_baseAddress.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Leaderboard URL must use HTTPS.", nameof(options));
        _ownsClient = ownsClient;
    }

    public async Task SendHeartbeatAsync(ParticipationHeartbeat heartbeat, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(_baseAddress, "v1/participation"))
        {
            Content = JsonContent.Create(heartbeat, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
    }

    public async Task<LeaderboardPage> GetPageAsync(LeaderboardPeriod period, int page, int pageSize, CancellationToken ct)
    {
        if (!Enum.IsDefined(period) || page < 1 || pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(page));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        var periodText = period.ToString().ToLowerInvariant();
        var uri = new Uri(_baseAddress, $"v1/leaderboards/{periodText}?page={page}&pageSize={pageSize}");
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LeaderboardPage>(JsonOptions, timeout.Token);
        if (!IsValidPage(result, period, page, pageSize))
            throw new InvalidDataException("The leaderboard returned an invalid page.");
        return result!;
    }

    public static bool IsValidPage(LeaderboardPage? value, LeaderboardPeriod period, int page, int pageSize) =>
        value is not null && value.Period == period && value.Page == page && value.PageSize == pageSize &&
        value.PeriodStartUtc.Offset == TimeSpan.Zero && value.PeriodEndUtc.Offset == TimeSpan.Zero &&
        value.PeriodEndUtc > value.PeriodStartUtc && value.GeneratedAtUtc != default &&
        value.GeneratedAtUtc.Offset == TimeSpan.Zero &&
        LeaderboardPeriodWindow.GetCurrent(period, value.PeriodStartUtc) ==
            (value.PeriodStartUtc, value.PeriodEndUtc) &&
        value.TotalEntries >= 0 && value.Entries is not null &&
        value.Entries.Count <= pageSize && value.Entries.Count <= value.TotalEntries &&
        value.Entries.All(entry => entry is not null && entry.Rank > 0 && entry.PlayerId > 0 &&
            !string.IsNullOrWhiteSpace(entry.Username) && entry.XpGained >= 0 &&
            entry.LastSampledAtUtc != default && entry.LastSampledAtUtc.Offset == TimeSpan.Zero);

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }
}
