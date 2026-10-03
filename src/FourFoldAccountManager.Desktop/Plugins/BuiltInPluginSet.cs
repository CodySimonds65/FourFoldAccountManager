using FourFoldAccountManager.Desktop.Services;

namespace FourFoldAccountManager.Desktop.Plugins;

// The four built-in plugins, with typed references for MainWindow and the list the sidebar hosts.
public sealed class BuiltInPluginSet
{
    public BuiltInPluginSet(TimerCoordinator timer)
    {
        Timer = new TimerPlugin(timer);
        All = Array.AsReadOnly<IFourFoldPlugin>([XpTracker, Stats, XpCalc, Timer]);
    }

    public XpTrackerPlugin XpTracker { get; } = new();

    public StatsPlugin Stats { get; } = new();

    public XpCalcPlugin XpCalc { get; } = new();

    public TimerPlugin Timer { get; }

    public IReadOnlyList<IFourFoldPlugin> All { get; }
}
