namespace FourFoldAccountManager.Core.Plugins;

public enum PluginTrust
{
    // A hub plugin without that clearance: declared sites only; anySite in the manifest is ignored.
    Standard,

    // Loaded from the dev plugins folder: the manifest is honored as written.
    Developer,

    // Listed on the hub and cleared by the maintainers for any website: anySite is honored.
    Verified
}
