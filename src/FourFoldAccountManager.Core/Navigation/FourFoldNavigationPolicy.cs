namespace FourFoldAccountManager.Core.Navigation;

public static class FourFoldNavigationPolicy
{
    private const string AllowedHost = "fourfoldonline.com";

    public static bool IsAllowed(Uri candidate) =>
        candidate.IsAbsoluteUri &&
        string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        candidate.IsDefaultPort &&
        string.IsNullOrEmpty(candidate.UserInfo) &&
        candidate.HostNameType == UriHostNameType.Dns &&
        string.Equals(candidate.IdnHost.TrimEnd('.'), AllowedHost, StringComparison.OrdinalIgnoreCase);

    // The pages the in-game store and gold buttons open.
    private static readonly string[] StorePaths = ["/shop.php", "/buy_gold.php"];

    public static bool IsStorePage(Uri candidate) =>
        IsAllowed(candidate) && StorePaths.Contains(candidate.AbsolutePath, StringComparer.OrdinalIgnoreCase);
}
