using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FourFoldAccountManager.Desktop.Updates;

public sealed class GitHubReleaseClient
{
    public const string Repository = "CodySimonds65/FourFoldAccountManager";
    public static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");

    private readonly HttpClient _httpClient;

    public GitHubReleaseClient(HttpClient httpClient, Version currentVersion)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FourFoldAccountManager", currentVersion.ToString(3)));
    }

    public async Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return UpdateReleaseParser.TryParse(json, out var release) ? release : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

public static class UpdateReleaseParser
{
    private static readonly Regex TagPattern = new(
        "^v(?<version>\\d+\\.\\d+\\.\\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private sealed record ReleaseJson(string? TagName, bool? Draft, bool? Prerelease, string? Body, List<AssetJson?>? Assets);

    private sealed record AssetJson(string? Name, string? BrowserDownloadUrl, long Size);

    public static bool TryParse(string json, out UpdateRelease? release)
    {
        release = null;

        try
        {
            if (JsonSerializer.Deserialize<ReleaseJson>(json, JsonOptions) is not { } root ||
                root.Draft != false ||
                root.Prerelease != false ||
                root.Assets is null)
            {
                return false;
            }

            var tagMatch = TagPattern.Match(root.TagName ?? string.Empty);
            if (!tagMatch.Success || !Version.TryParse(tagMatch.Groups["version"].Value, out var version))
            {
                return false;
            }

            var assets = new List<UpdateAsset>();
            foreach (var asset in root.Assets)
            {
                if (asset is null ||
                    string.IsNullOrWhiteSpace(asset.Name) ||
                    !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUrl) ||
                    downloadUrl.Scheme != Uri.UriSchemeHttps ||
                    asset.Size <= 0)
                {
                    return false;
                }

                assets.Add(new UpdateAsset(asset.Name, downloadUrl, asset.Size));
            }

            var releaseVersion = version.ToString(3);
            var standaloneName = $"FourFoldAccountManager-v{releaseVersion}-win-x64-standalone.exe";
            var checksumName = $"FourFoldAccountManager-v{releaseVersion}-checksums.txt";
            if (assets.Count(asset => string.Equals(asset.Name, standaloneName, StringComparison.Ordinal)) != 1 ||
                assets.Count(asset => string.Equals(asset.Name, checksumName, StringComparison.Ordinal)) != 1)
            {
                return false;
            }

            release = new UpdateRelease(version, root.Body ?? string.Empty, assets);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
