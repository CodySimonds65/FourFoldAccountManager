namespace FourFoldAccountManager.Core.Plugins.Hub;

public static class HubHttp
{
    // Downloads a file of at most maximumBytes over https. Null when the request fails, ends anywhere but https, or
    // the file is larger. The bytes are counted as they arrive, since a response needn't say how long it is.
    public static async Task<byte[]?> DownloadAsync(
        HttpClient http, Uri uri, long maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode ||
                response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps ||
                response.Content.Headers.ContentLength > maximumBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maximumBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }
}
