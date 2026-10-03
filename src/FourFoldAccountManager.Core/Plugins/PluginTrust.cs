namespace FourFoldAccountManager.Core.Plugins;

public enum PluginTrust
{
    // Declared sites only; anySite in the manifest is ignored.
    Standard,

    // Loaded from the dev plugins folder: the manifest is honored as written.
    Developer,

    // Reviewed and signed by FourFold's maintainers (part 3): anySite is honored.
    Verified
}
