using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Plugins;

public static class BuiltInPlugins
{
    public const string XpTrackerId = "xp-tracker";
    public const string StatsId = "stats";
    public const string XpCalcId = "xp-calc";
    public const string TimerId = "timer";

    // Icons are Segoe glyphs: AreaChart, ReportDocument, Calculator, Stopwatch.
    public static PluginDescriptor XpTracker { get; } =
        new(XpTrackerId, "XP tracker", "XP", "", [OverlayAddOnKind.Xp], []);

    public static PluginDescriptor Stats { get; } =
        new(StatsId, "Stats", "Stats", "", [OverlayAddOnKind.Stats], []);

    public static PluginDescriptor XpCalc { get; } =
        new(XpCalcId, "XP calc", "XP calc", "", [OverlayAddOnKind.XpCalc], []);

    public static PluginDescriptor Timer { get; } =
        new(TimerId, "Timer", "Timer", "", [OverlayAddOnKind.Timer],
            [GlobalShortcutAction.TimerSplit, GlobalShortcutAction.TimerFinish, GlobalShortcutAction.TimerReset]);

    // Default strip order.
    public static IReadOnlyList<PluginDescriptor> All { get; } = Array.AsReadOnly([XpTracker, Stats, XpCalc, Timer]);
}
