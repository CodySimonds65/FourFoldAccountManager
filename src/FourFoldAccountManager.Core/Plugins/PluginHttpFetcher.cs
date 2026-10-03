using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FourFoldAccountManager.Core.Plugins;

public sealed record PluginHttpRequest(
    string Url,
    string? Method,
    IReadOnlyDictionary<string, string>? Headers,
    string? Body);

public sealed record PluginHttpResponse(int Status, IReadOnlyDictionary<string, string> Headers, string Text);

// Makes a web request on a plugin's behalf, because most sites refuse cross-origin requests from a page. Every URL
// (and every redirect) passes the same site rules as the plugin's page, no cookies or credentials are ever attached,
// and the connection only goes to an address checked to be outside the local network.
public sealed class PluginHttpFetcher : IDisposable
{
    public const int MaximumResponseBytes = 2 * 1024 * 1024;

    public const int MaximumRedirects = 5;

    public const int MaximumRequestsPerMinute = 60;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private const string HeaderSeparators = "()<>@,;:\\\"/[]?={}";

    private static readonly string[] ForbiddenHeaders =
        ["Cookie", "Host", "Content-Length", "Transfer-Encoding", "Connection"];

    private readonly PluginManifest _manifest;
    private readonly PluginTrust _trust;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _timeout;
    private readonly Queue<DateTimeOffset> _recent = new();

    public PluginHttpFetcher(
        PluginManifest manifest, PluginTrust trust, HttpMessageHandler? handler = null, TimeProvider? clock = null,
        TimeSpan? timeout = null)
    {
        _manifest = manifest;
        _trust = trust;
        _clock = clock ?? TimeProvider.System;
        _timeout = timeout ?? RequestTimeout;
        _http = new HttpClient(handler ?? CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<PluginHttpResponse> FetchAsync(
        PluginHttpRequest request, CancellationToken cancellationToken = default)
    {
        var method = (request.Method ?? "GET").ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            _ => throw new PluginApiException("invalid-argument", "Only GET and POST requests are supported.")
        };
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            throw new PluginApiException("invalid-argument", "The URL isn't valid.");
        }

        ValidateHeaders(request.Headers);
        EnsureAllowed(uri);
        TakeRateSlot();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var body = request.Body;
        var originalHost = uri.IdnHost;
        var originalPort = uri.Port;
        var forwardHeaders = true;
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                using var message = new HttpRequestMessage(method, uri);
                if (method == HttpMethod.Post)
                {
                    message.Content = new StringContent(body ?? string.Empty, Encoding.UTF8);
                }

                // Don't forward plugin headers to a different host or port; once headers are dropped, keep them dropped.
                if (uri.Port != originalPort ||
                    !uri.IdnHost.Equals(originalHost, StringComparison.OrdinalIgnoreCase))
                {
                    forwardHeaders = false;
                }

                ApplyHeaders(message, forwardHeaders ? request.Headers : null);
                using var response = await _http.SendAsync(
                    message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 &&
                    response.Headers.Location is { } location)
                {
                    if (redirects >= MaximumRedirects)
                    {
                        throw new PluginApiException("limit-exceeded", "The request was redirected too many times.");
                    }

                    if (!Uri.TryCreate(uri, location, out var next))
                    {
                        throw new PluginApiException("unavailable", "The site sent a redirect that isn't valid.");
                    }

                    uri = next;
                    EnsureAllowed(uri);
                    TakeRateSlot();
                    if ((int)response.StatusCode == 303 ||
                        (method == HttpMethod.Post && (int)response.StatusCode is 301 or 302))
                    {
                        method = HttpMethod.Get;
                        body = null;
                    }

                    continue;
                }

                var text = await ReadTextAsync(response, timeout.Token);
                text = text.TrimStart('\uFEFF');
                return new PluginHttpResponse(
                    (int)response.StatusCode, FlattenHeaders(response), text);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PluginApiException("unavailable", "The request took too long.");
        }
        catch (HttpRequestException exception) when (exception.InnerException is PluginApiException refused)
        {
            throw new PluginApiException(refused.Code, refused.Message);
        }
        catch (HttpRequestException)
        {
            throw new PluginApiException("unavailable", "The request failed.");
        }
        catch (Exception exception) when (exception is not PluginApiException and not OperationCanceledException)
        {
            throw new PluginApiException("unavailable", "The request failed.");
        }
    }

    // A header name must be an HTTP token, and a value printable ASCII or a tab: no line breaks, control characters
    // or non-ASCII, any of which could smuggle extra headers or a second request onto the connection.
    private static void ValidateHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            var validName = name.Length > 0 &&
                            name.All(character => character is > ' ' and < '\x7f' && !HeaderSeparators.Contains(character));
            var validValue = value is not null &&
                             value.All(character => character == '\t' || character is >= ' ' and <= '~');
            if (!validName || !validValue)
            {
                throw new PluginApiException("invalid-argument", "A request header's name or value isn't valid.");
            }
        }
    }

    // Refuses a host when any address it resolves to is on the local network, and returns all addresses outside
    // the local network, so the addresses that were checked are the addresses that are used.
    public static async Task<IPAddress[]> ResolvePublicAddressesAsync(
        string host,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken cancellationToken)
    {
        var addresses = await resolve(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(PluginNetworkPolicy.IsLocalAddress))
        {
            throw new PluginApiException("site-not-allowed", "Plugins can't reach the local network.");
        }

        return addresses;
    }

    public void Dispose() => _http.Dispose();

    private void EnsureAllowed(Uri uri)
    {
        if (PluginNetworkPolicy.IsOwnOrigin(uri, _manifest.Id) ||
            !PluginNetworkPolicy.IsAllowed(uri, _manifest, _trust))
        {
            throw new PluginApiException(
                "site-not-allowed", $"{uri.Host} isn't one of this plugin's declared sites.");
        }
    }

    private void TakeRateSlot()
    {
        lock (_recent)
        {
            var now = _clock.GetUtcNow();
            while (_recent.Count > 0 && now - _recent.Peek() >= TimeSpan.FromSeconds(60))
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= MaximumRequestsPerMinute)
            {
                throw new PluginApiException("limit-exceeded", "A plugin can make at most 60 requests a minute.");
            }

            _recent.Enqueue(now);
        }
    }

    private static void ApplyHeaders(HttpRequestMessage message, IReadOnlyDictionary<string, string>? headers)
    {
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            if (ForbiddenHeaders.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Content headers (such as Content-Type) belong on the body, not the request.
            if (!message.Headers.TryAddWithoutValidation(name, value) && message.Content is not null)
            {
                message.Content.Headers.Remove(name);
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }

    private static Dictionary<string, string> FlattenHeaders(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            if (!name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                result[name] = string.Join(", ", values);
            }
        }

        return result;
    }

    private static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new PluginApiException("limit-exceeded", "The response is larger than 2 MB.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
            {
                throw new PluginApiException("limit-exceeded", "The response is larger than 2 MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    // No cookies, no redirects the fetcher hasn't checked, no system proxy (the address check must see the real
    // host), and a connection only to addresses outside the local network.
    private static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await ResolvePublicAddressesAsync(
                context.DnsEndPoint.Host, Dns.GetHostAddressesAsync, cancellationToken);

            Exception? lastException = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (OperationCanceledException)
                {
                    socket.Dispose();
                    throw;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    socket.Dispose();
                }
            }

            throw lastException ?? new SocketException();
        }
    };
}
