using System.Text.RegularExpressions;

namespace FourFoldAccountManager.Core.Plugins;

// A community plugin's card is identified by "<pluginId>/<cardId>", for example "cody.goal-tracker/goal".
public static partial class PluginCardId
{
    [GeneratedRegex("^" + PluginManifestReader.IdPattern + "/" + PluginManifestReader.CardIdPattern + @"\z")]
    private static partial Regex Pattern();

    public static string Create(string pluginId, string cardId) => $"{pluginId}/{cardId}";

    public static bool IsValid(string? value) => value is { Length: <= 97 } && Pattern().IsMatch(value);
}
