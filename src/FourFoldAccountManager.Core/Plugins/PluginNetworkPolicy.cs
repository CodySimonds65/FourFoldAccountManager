using System.Net;
using System.Net.Sockets;

namespace FourFoldAccountManager.Core.Plugins;

// What a community plugin may load or contact. The same rules back the Content-Security-Policy header, the request
// handler's second check, and the FourFold-run fetch.
public static class PluginNetworkPolicy
{
    // Every plugin page lives under this made-up top-level name, one domain per plugin.
    public const string OriginSuffix = "fourfoldplugin";

    // "--" can't appear in a plugin id, so replacing the dots keeps every id's host unique.
    public static string OriginHost(string pluginId) => $"{pluginId.Replace(".", "--")}.{OriginSuffix}";

    public static Uri Origin(string pluginId) => new($"https://{OriginHost(pluginId)}/");

    public static bool IsOwnOrigin(Uri uri, string pluginId) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.Host.Equals(OriginHost(pluginId), StringComparison.OrdinalIgnoreCase);

    // True for any host in the plugin namespace, whichever plugin it belongs to.
    public static bool IsPluginHost(string host)
    {
        var name = host.TrimEnd('.');
        return name.Equals(OriginSuffix, StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("." + OriginSuffix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool AllowsAnySite(PluginManifest manifest, PluginTrust trust) =>
        manifest.AnySite && trust is (PluginTrust.Developer or PluginTrust.Verified);

    // Loopback, private, link-local, carrier-grade NAT and unspecified addresses, in any spelling: a plugin must never
    // reach the user's own network.
    public static bool IsLocalAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Rebuilding from the bytes drops any scope id, so "::1%1" is still loopback.
            var bytes = address.GetAddressBytes();
            address = new IPAddress(bytes);
            if (address.IsIPv4MappedToIPv6)
            {
                return IsLocalAddress(address.MapToIPv4());
            }

            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal ||
                address.IsIPv6UniqueLocal || address.IsIPv6SiteLocal)
            {
                return true;
            }

            // An IPv6 address that embeds an IPv4 address reaches that IPv4 host: IPv4-compatible (::a.b.c.d),
            // SIIT (::ffff:0:a.b.c.d), NAT64 (64:ff9b::a.b.c.d) and 6to4 (2002:AABB:CCDD::).
            byte[]? embedded =
                AllZero(bytes, 0, 12) ? bytes[12..16]
                : AllZero(bytes, 0, 8) && bytes[8] == 0xff && bytes[9] == 0xff && bytes[10] == 0 && bytes[11] == 0 ? bytes[12..16]
                : bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b && AllZero(bytes, 4, 8) ? bytes[12..16]
                : bytes[0] == 0x20 && bytes[1] == 0x02 ? bytes[2..6]
                : null;
            return embedded is not null && IsLocalAddress(new IPAddress(embedded));
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 or 10 or 127 => true,
            100 => octets[1] is >= 64 and <= 127,
            169 => octets[1] == 254,
            172 => octets[1] is >= 16 and <= 31,
            192 => octets[1] == 168,
            _ => false
        };

        static bool AllZero(byte[] value, int start, int count) => value.AsSpan(start, count).IndexOfAnyExcept((byte)0) < 0;
    }

    public static bool IsLocalHost(string host)
    {
        var normalized = host.Trim('[', ']').TrimEnd('.');
        return PluginNetworkHosts.IsLocalName(normalized) ||
               (IPAddress.TryParse(normalized, out var address) && IsLocalAddress(address));
    }

    // isScript is true for script and worker requests, which may only ever come from the plugin's own files.
    public static bool IsAllowed(Uri uri, PluginManifest manifest, PluginTrust trust, bool isScript = false)
    {
        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (IsOwnOrigin(uri, manifest.Id))
        {
            return true;
        }

        if (IsPluginHost(uri.IdnHost))
        {
            return false;
        }

        if (isScript || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || IsLocalHost(uri.IdnHost))
        {
            return false;
        }

        return AllowsAnySite(manifest, trust) ||
               manifest.Sites.Any(site =>
                   site.Port == uri.Port && site.IdnHost.Equals(uri.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildContentSecurityPolicy(PluginManifest manifest, PluginTrust trust)
    {
        var allowsAnySite = AllowsAnySite(manifest, trust);
        var wssSites = string.Join(' ', manifest.Sites.Select(site =>
            site.IsDefaultPort ? $"wss://{site.IdnHost}" : $"wss://{site.IdnHost}:{site.Port}"));

        string images;
        string connectHttps;

        if (allowsAnySite)
        {
            images = "https:";
            connectHttps = "https:";
        }
        else
        {
            var originParts = manifest.Sites.Select(site =>
                site.IsDefaultPort ? $"https://{site.IdnHost}" : $"https://{site.IdnHost}:{site.Port}").ToArray();
            images = string.Join(' ', originParts);
            connectHttps = string.Join(' ', originParts);
        }

        var connect = Prefix(connectHttps) + Prefix(wssSites);
        return "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; " +
               $"img-src 'self' data:{Prefix(images)}; connect-src 'self'{connect}; " +
               "frame-src 'none'; worker-src 'none'; webrtc 'block'; object-src 'none'; base-uri 'none'; form-action 'none'";

        static string Prefix(string value) => value.Length == 0 ? string.Empty : " " + value;
    }
}
