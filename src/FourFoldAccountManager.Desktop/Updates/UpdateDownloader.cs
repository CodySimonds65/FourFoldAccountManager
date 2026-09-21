using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FourFoldAccountManager.Desktop.Updates;

public sealed class UpdateDownloader : IUpdateDownloader
{
    private const long ChecksumMaximumBytes = 32 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string _temporaryDirectory;
    private readonly long _maximumBytes;

    public UpdateDownloader(HttpClient httpClient, string? temporaryDirectory = null, long maximumBytes = 256L * 1024 * 1024)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        _httpClient = httpClient;
        _temporaryDirectory = temporaryDirectory ?? Path.GetTempPath();
        _maximumBytes = maximumBytes;
        Directory.CreateDirectory(_temporaryDirectory);
    }

    public async Task<string?> DownloadAndVerifyAsync(UpdateRelease release, CancellationToken cancellationToken)
    {
        var releaseVersion = release.Version.ToString(3);
        var standaloneName = $"FourFoldAccountManager-v{releaseVersion}-win-x64-standalone.exe";
        var checksumName = $"FourFoldAccountManager-v{releaseVersion}-checksums.txt";
        var standaloneAsset = FindExactlyOne(release.Assets, standaloneName);
        var checksumAsset = FindExactlyOne(release.Assets, checksumName);
        if (standaloneAsset is null || checksumAsset is null ||
            standaloneAsset.Size > _maximumBytes ||
            !IsHttps(standaloneAsset.DownloadUrl) ||
            !IsHttps(checksumAsset.DownloadUrl))
        {
            return null;
        }

        try
        {
            var expectedHash = await DownloadChecksumAsync(checksumAsset.DownloadUrl, standaloneName, cancellationToken);
            if (expectedHash is null)
            {
                return null;
            }

            var downloadPath = Path.Combine(_temporaryDirectory, $"FourFoldAccountManager-{Guid.NewGuid():N}.download");
            var keepDownloadedFile = false;
            try
            {
                if (!await DownloadFileAsync(standaloneAsset.DownloadUrl, downloadPath, _maximumBytes, cancellationToken))
                {
                    return null;
                }

                await using var stream = File.OpenRead(downloadPath);
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                keepDownloadedFile = true;
                return downloadPath;
            }
            finally
            {
                if (!keepDownloadedFile)
                {
                    DeleteIfPresent(downloadPath);
                }
            }
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<string?> DownloadChecksumAsync(Uri checksumUrl, string standaloneName, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(checksumUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > ChecksumMaximumBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(content) > ChecksumMaximumBytes)
        {
            return null;
        }

        var matches = new List<string>();
        foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOfAny(new[] { ' ', '\t' });
            if (separator <= 0)
            {
                continue;
            }

            var hash = line[..separator].Trim();
            var fileName = line[separator..].Trim();
            if (fileName.StartsWith('*'))
            {
                fileName = fileName[1..];
            }

            if (hash.Length == 64 && hash.All(Uri.IsHexDigit) &&
                string.Equals(fileName, standaloneName, StringComparison.Ordinal))
            {
                matches.Add(hash);
            }
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private async Task<bool> DownloadFileAsync(Uri url, string path, long maximumBytes, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode ||
            (response.Content.Headers.ContentLength is long contentLength && contentLength > maximumBytes))
        {
            return false;
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(path);
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > maximumBytes)
            {
                return false;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return totalBytes > 0;
    }

    private static UpdateAsset? FindExactlyOne(IReadOnlyList<UpdateAsset> assets, string name)
    {
        UpdateAsset? match = null;
        foreach (var asset in assets)
        {
            if (!string.Equals(asset.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = asset;
        }

        return match;
    }

    private static bool IsHttps(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps;

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
