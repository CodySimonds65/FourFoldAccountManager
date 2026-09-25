using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Panel;

public static class PluginSelectionPolicy
{
    public static PluginKind Normalize(PluginKind? plugin) => plugin is PluginKind.XpTracker or
        PluginKind.ClassComparison or PluginKind.XpCalculator or PluginKind.Timer ? plugin.Value : PluginKind.XpTracker;
}
