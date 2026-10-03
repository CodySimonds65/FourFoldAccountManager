namespace FourFoldAccountManager.Core.Plugins.Hub;

// The size limits the hub tool enforces when it builds a package and the app enforces again when it installs one.
public static class HubLimits
{
    public const int MaximumCatalogBytes = 1024 * 1024;

    public const int MaximumCatalogPlugins = 2000;

    public const int MaximumPackageFiles = 500;

    public const long MaximumUnpackedBytes = 5 * 1024 * 1024;

    // A package is a zip of at most MaximumUnpackedBytes, so an honest one is never larger than this.
    public const long MaximumPackageBytes = 6 * 1024 * 1024;
}
