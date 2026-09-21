namespace FourFoldAccountManager.Core.Models;

public sealed record GameViewportSize(double WidthPercent, double HeightPercent)
{
    public const double MinimumPercent = 25;
    public const double MaximumPercent = 100;

    public static GameViewportSize Default { get; } = new(MaximumPercent, MaximumPercent);

    public bool IsValid =>
        double.IsFinite(WidthPercent) &&
        double.IsFinite(HeightPercent) &&
        WidthPercent is >= MinimumPercent and <= MaximumPercent &&
        HeightPercent is >= MinimumPercent and <= MaximumPercent;
}
