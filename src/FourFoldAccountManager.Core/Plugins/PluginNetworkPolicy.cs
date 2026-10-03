using System.Net;
using System.Net.Sockets;

namespace FourFoldAccountManager.Core.Plugins;

// What a community plugin may load or contact. The same rules back the Content-Security-Policy header, the request
// handler's second check, and the FourFold-run fetch.
public static class PluginNetworkPolicy
{
    public static string OriginHost(string pluginId) => $"{pluginId}.plugin.fourfold";

    public static Uri Origin(string pluginId) => new($"https://{OriginHost(pluginId)}/");

    public static bool IsOwnOrigin(Uri uri, string pluginId) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.Host.Equals(OriginHost(pluginId), StringComparison.OrdinalIgnoreCase);

    public static bool AllowsAnySite(PluginManifest manifest, PluginTrust trust) =>
        manifest.AnySite && trust is PluginTrust.Developer or PluginTrust.Verified;

    // Loopback, private, link-local and unspecified addresses: a plugin must never reach the user's own network.
    public static bool IsLocalAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || address.IsIPv6SiteLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 or 10 or 127 => true,
            172 => bytes[1] is >= 16 and <= 31,
            192 => bytes[1] == 168,
            169 => bytes[1] == 254,
            _ => false
        };
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
        string images;
        string connections;
        if (AllowsAnySite(manifest, trust))
        {
            images = "https:";
            connections = "https: wss:";
        }
        else
        {
            var origins = manifest.Sites.Select(site => site.GetLeftPart(UriPartial.Authority)).ToArray();
            images = string.Join(' ', origins);
            connections = string.Join(' ', origins.Concat(origins.Select(origin => "wss" + origin["https".Length..])));
        }

        return "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; " +
               $"img-src 'self' data:{Prefix(images)}; connect-src 'self'{Prefix(connections)}; " +
               "frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";

        static string Prefix(string value) => value.Length == 0 ? string.Empty : " " + value;
    }
}
