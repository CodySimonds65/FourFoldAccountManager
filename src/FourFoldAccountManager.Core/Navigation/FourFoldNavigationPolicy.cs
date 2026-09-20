using System.Globalization;

namespace FourFoldAccountManager.Core.Navigation;

public sealed class FourFoldNavigationPolicy
{
    private readonly HashSet<string> _allowedHosts;

    public FourFoldNavigationPolicy(IEnumerable<string> allowedHosts)
    {
        ArgumentNullException.ThrowIfNull(allowedHosts);
        _allowedHosts = new HashSet<string>(allowedHosts.Select(NormalizeHost), StringComparer.OrdinalIgnoreCase);
        if (_allowedHosts.Count == 0)
        {
            throw new ArgumentException("At least one allowed host is required.", nameof(allowedHosts));
        }
    }

    public bool TryValidate(Uri candidate, out Uri approved)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        approved = null!;

        if (!candidate.IsAbsoluteUri ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !candidate.IsDefaultPort ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            candidate.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        var host = candidate.IdnHost.TrimEnd('.');
        if (!_allowedHosts.Contains(host))
        {
            return false;
        }

        approved = candidate;
        return true;
    }

    private static string NormalizeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) ||
            !string.Equals(host, host.Trim(), StringComparison.Ordinal) ||
            host.Contains('/') || host.Contains('\\') || host.Contains('@') ||
            host.Contains(':') || host.Contains('?') || host.Contains('#') ||
            host.Contains('*') || Uri.CheckHostName(host.TrimEnd('.')) != UriHostNameType.Dns)
        {
            throw new ArgumentException("Allowed hosts must be DNS hostnames without schemes, ports, or paths.", nameof(host));
        }

        try
        {
            return new IdnMapping().GetAscii(host.TrimEnd('.')).ToLowerInvariant();
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("Allowed host is not a valid DNS hostname.", nameof(host), exception);
        }
    }
}
