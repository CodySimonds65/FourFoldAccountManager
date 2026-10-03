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
}
