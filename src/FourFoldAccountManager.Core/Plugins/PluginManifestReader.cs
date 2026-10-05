using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Core.Plugins;

public static partial class PluginManifestReader
{
    // 2 added fourfold.profile, and equipment on stats.get.
    public const int SupportedApiVersion = 2;

    // author.plugin-name: lowercase letters, digits and dashes in dot-separated parts, with at least one dot.
    public const string IdPattern = @"[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)+";

    public const string CardIdPattern = "[a-z0-9]+(-[a-z0-9]+)*";

    // What a plugin that asks for a newer apiVersion is told. The hub's install step matches on it.
    public const string NeedsNewerFourFold = "Update FourFold to use this plugin.";

    private const int MaximumIconBytes = 64 * 1024;

    private const int MaximumManifestBytes = 64 * 1024;

    [GeneratedRegex("^" + IdPattern + @"\z")]
    private static partial Regex IdRegex();

    [GeneratedRegex("^" + CardIdPattern + @"\z")]
    private static partial Regex CardIdRegex();

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+\z")]
    private static partial Regex VersionRegex();

    // Windows treats these names as devices whatever follows the dot, so a plugin called nul.tools would get a storage
    // file "nul.tools.json" that silently swallows every write.
    [GeneratedRegex("^(con|prn|aux|nul|com[0-9]|lpt[0-9])$")]
    private static partial Regex DeviceNameRegex();

    // True for an id the reader accepts: the pattern, no Windows device name first, and a usable host label.
    public static bool IsValidId([NotNullWhen(true)] string? id)
    {
        if (id is null || id.Length > 64 || !IdRegex().IsMatch(id) || DeviceNameRegex().IsMatch(id[..id.IndexOf('.')]))
        {
            return false;
        }

        var label = id.Replace(".", "--");
        return label.Length <= 63 && !label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVersion([NotNullWhen(true)] string? version) =>
        version is not null && VersionRegex().IsMatch(version);

    // Reads and validates <folder>/plugin.json. Every rejection carries a reason an author can act on, and a file the
    // reader can't make sense of for any other reason (one that vanishes mid-read, a host name Uri refuses) is
    // rejected too: this never throws.
    public static PluginManifestResult Read(string folder)
    {
        try
        {
            return ReadManifest(folder);
        }
        catch (Exception)
        {
            return Reject("plugin.json couldn't be read.");
        }
    }

    private static PluginManifestResult ReadManifest(string folder)
    {
        var path = Path.Combine(folder, "plugin.json");
        if (!File.Exists(path))
        {
            return Reject("plugin.json is missing.");
        }

        if (new FileInfo(path).Length > MaximumManifestBytes)
        {
            return Reject("plugin.json is too large.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return Reject("plugin.json isn't valid JSON.");
        }

        try
        {
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Reject("plugin.json isn't valid JSON.");
                }

                var id = Text(root, "id");
                if (id is not null && id.Length <= 64 && IdRegex().IsMatch(id) &&
                    DeviceNameRegex().IsMatch(id[..id.IndexOf('.')]))
                {
                    return Reject("The id can't start with a Windows device name such as nul, con or com1.");
                }

                if (!IsValidId(id))
                {
                    return Reject("The id must look like author.plugin-name (lowercase letters, digits, dots and dashes).");
                }

                if (!InRange(Text(root, "name"), 1, 40, out var name))
                {
                    return Reject("The name must be 1 to 40 characters.");
                }

                if (!InRange(Text(root, "shortLabel"), 1, 8, out var shortLabel))
                {
                    return Reject("The shortLabel must be 1 to 8 characters.");
                }

                var version = Text(root, "version");
                if (!IsVersion(version))
                {
                    return Reject("The version must look like 1.0.0.");
                }

                if (!InRange(Text(root, "author"), 1, 40, out var author))
                {
                    return Reject("The author must be 1 to 40 characters.");
                }

                var description = Text(root, "description") ?? string.Empty;
                if (description.Length > 200)
                {
                    return Reject("The description must be at most 200 characters.");
                }

                if (new[] { name, shortLabel, author, description }.Any(PluginText.HasUnsafeCharacter))
                {
                    return Reject("The name, shortLabel, author and description can't contain control characters or invisible formatting characters.");
                }

                if (!root.TryGetProperty("apiVersion", out var apiElement) || apiElement.ValueKind != JsonValueKind.Number ||
                    !apiElement.TryGetInt32(out var apiVersion) || apiVersion < 1)
                {
                    return Reject("The apiVersion must be a whole number.");
                }

                if (apiVersion > SupportedApiVersion)
                {
                    return Reject(NeedsNewerFourFold);
                }

                var panel = Text(root, "panel");
                if (panel is null || panel.Contains('\\') || !panel.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                    !PluginPaths.TryResolveInside(folder, panel, out var panelPath) || !File.Exists(panelPath))
                {
                    return Reject("The panel must be an .html file inside the plugin folder.");
                }

                var icon = Text(root, "icon");
                if (icon is not null &&
                    (icon.Contains('\\') || !icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                     !PluginPaths.TryResolveInside(folder, icon, out var iconPath) || !File.Exists(iconPath) ||
                     new FileInfo(iconPath).Length > MaximumIconBytes || !IsSmallPng(iconPath)))
                {
                    return Reject("The icon must be a PNG inside the plugin folder, at most 256 by 256 pixels and 64 KB.");
                }

                if (!TryReadSites(root, out var sites, out var siteError))
                {
                    return Reject(siteError);
                }

                var anySite = root.TryGetProperty("anySite", out var anySiteElement) &&
                              anySiteElement.ValueKind == JsonValueKind.True;
                if (!TryReadCards(root, out var cards, out var cardError))
                {
                    return Reject(cardError);
                }

                return new PluginManifestResult(
                    new PluginManifest(id, name, shortLabel, version, author, description, apiVersion, panel, icon,
                        sites, anySite, cards) { Folder = folder },
                    null);
            }
        }
        catch (InvalidOperationException)
        {
            return Reject("plugin.json isn't valid JSON.");
        }
    }

    private static PluginManifestResult Reject(string reason) => new(null, reason);

    internal static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim()
            : null;

    internal static bool InRange(string? value, int minimum, int maximum, out string result)
    {
        result = value ?? string.Empty;
        return result.Length >= minimum && result.Length <= maximum;
    }

    // The app decodes the icon on every render of the strip, and a tiny file can declare an enormous image (a 274-byte
    // PNG declaring 1 by 100000 pixels made it commit 215 MB), so the header is checked first, not the file's content.
    // A PNG's first 24 bytes are the signature, then the IHDR chunk's length and name, then its width and height.
    private static bool IsSmallPng(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length &&
               header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
               header[12..16].SequenceEqual("IHDR"u8) &&
               BinaryPrimitives.ReadUInt32BigEndian(header[16..]) is >= 1 and <= 256 &&
               BinaryPrimitives.ReadUInt32BigEndian(header[20..]) is >= 1 and <= 256;
    }

    internal static bool TryReadSites(JsonElement root, out IReadOnlyList<Uri> sites, out string error)
    {
        sites = [];
        error = "Each site must look like https://example.com, with no path, and can't be an IP address or a local network name.";
        if (!root.TryGetProperty("sites", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var result = new List<Uri>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(item.GetString(), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.AbsolutePath != "/" || uri.Query.Length > 0 ||
                uri.Fragment.Length > 0 || uri.UserInfo.Length > 0 || uri.HostNameType != UriHostNameType.Dns ||
                PluginNetworkHosts.IsLocalName(uri.IdnHost) || uri.IdnHost.EndsWith('.') ||
                IsIpLikeHost(uri.IdnHost) || !IsPlainHost(uri.IdnHost) || PluginNetworkPolicy.IsPluginHost(uri.IdnHost))
            {
                return false;
            }

            result.Add(new Uri(uri.GetLeftPart(UriPartial.Authority)));
        }

        if (result.Count > 10)
        {
            error = "A plugin can declare at most 10 sites.";
            return false;
        }

        sites = Array.AsReadOnly(result.Distinct().ToArray());
        return true;
    }

    // What a real host name is once converted: letters, digits, dots and dashes (a name with an umlaut becomes an xn--
    // name). Uri's conversion also turns look-alike characters into punctuation (U+FE64 becomes "<", U+FF5C becomes
    // "|"), and a site named like that would rearrange the review summary and every other place a site is shown.
    private static bool IsPlainHost(string host) =>
        host.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-');

    private static bool IsIpLikeHost(string host)
    {
        var lastLabel = host.Split('.').LastOrDefault() ?? string.Empty;
        return lastLabel.Length > 0 && lastLabel.All(c => c >= '0' && c <= '9');
    }

    private static bool TryReadCards(JsonElement root, out IReadOnlyList<PluginCardManifest> cards, out string error)
    {
        cards = [];
        error = "Each card needs an id (lowercase letters, digits, dashes), a name (1 to 24 characters, no control or invisible formatting characters) and a scope of account or global.";
        if (!root.TryGetProperty("cards", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var result = new List<PluginCardManifest>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var id = Text(item, "id");
            var scope = Text(item, "scope");
            if (id is null || id.Length > 32 || !CardIdRegex().IsMatch(id) ||
                !InRange(Text(item, "name"), 1, 24, out var name) || PluginText.HasUnsafeCharacter(name) ||
                scope is not ("account" or "global") || result.Any(card => card.Id == id))
            {
                return false;
            }

            result.Add(new PluginCardManifest(id, name,
                scope == "account" ? OverlayAddOnScope.Account : OverlayAddOnScope.Global));
        }

        if (result.Count > 6)
        {
            error = "A plugin can declare at most 6 cards.";
            return false;
        }

        cards = Array.AsReadOnly(result.ToArray());
        return true;
    }
}

// Host-name checks shared by the manifest reader and the network policy.
public static class PluginNetworkHosts
{
    // Names that only mean something on the user's own network: localhost, mDNS (.local), common intranet endings, and
    // a single-label name such as "router". An IPv6 literal has colons and no dots, so it isn't a single-label name.
    public static bool IsLocalName(string host)
    {
        var trimmed = host.TrimEnd('.');
        return trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               trimmed.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
               trimmed.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
               trimmed.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
               trimmed.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
               trimmed.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase) ||
               (trimmed.Length > 0 && !trimmed.Contains('.') && !trimmed.Contains(':'));
    }
}
