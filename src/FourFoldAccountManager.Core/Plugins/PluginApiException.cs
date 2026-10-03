namespace FourFoldAccountManager.Core.Plugins;

// A plugin API call that was refused. Code is one of: invalid-argument, not-declared, site-not-allowed,
// limit-exceeded, unavailable. The message is shown to the plugin's author.
public sealed class PluginApiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
