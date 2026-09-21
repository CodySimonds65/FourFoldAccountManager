using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FourFoldAccountManager.Desktop.Updates;

public sealed class GitHubReleaseClient : IUpdateReleaseClient
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
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
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

    public static bool TryParse(string json, out UpdateRelease? release)
    {
        release = null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "tag_name", out var tagName) ||
                !TryGetBoolean(root, "draft", out var isDraft) ||
                !TryGetBoolean(root, "prerelease", out var isPrerelease) ||
                isDraft ||
                isPrerelease)
            {
                return false;
            }

            var tagMatch = TagPattern.Match(tagName);
            if (!tagMatch.Success || !Version.TryParse(tagMatch.Groups["version"].Value, out var version))
            {
                return false;
            }

            if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var assets = new List<UpdateAsset>();
            foreach (var assetElement in assetsElement.EnumerateArray())
            {
                if (assetElement.ValueKind != JsonValueKind.Object ||
                    !TryGetString(assetElement, "name", out var assetName) ||
                    !TryGetString(assetElement, "browser_download_url", out var downloadUrlText) ||
                    !Uri.TryCreate(downloadUrlText, UriKind.Absolute, out var downloadUrl) ||
                    downloadUrl.Scheme != Uri.UriSchemeHttps ||
                    !assetElement.TryGetProperty("size", out var sizeElement) ||
                    !sizeElement.TryGetInt64(out var size) ||
                    size <= 0)
                {
                    return false;
                }

                assets.Add(new UpdateAsset(assetName, downloadUrl, size));
            }

            var releaseVersion = version.ToString(3);
            var standaloneName = $"FourFoldAccountManager-v{releaseVersion}-win-x64-standalone.exe";
            var checksumName = $"FourFoldAccountManager-v{releaseVersion}-checksums.txt";
            if (assets.Count(asset => string.Equals(asset.Name, standaloneName, StringComparison.Ordinal)) != 1 ||
                assets.Count(asset => string.Equals(asset.Name, checksumName, StringComparison.Ordinal)) != 1)
            {
                return false;
            }

            var name = TryGetString(root, "name", out var releaseName) ? releaseName : tagName;
            var notes = TryGetString(root, "body", out var releaseNotes) ? releaseNotes : string.Empty;
            release = new UpdateRelease(version, tagName, name, notes, assets);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var property) ||
            (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}
