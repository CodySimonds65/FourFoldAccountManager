using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// Where the stats window sits on the desktop, in WPF device-independent units. Width and Height are the
// restored size even when the window was maximized or minimized.
public sealed record StatsWindowPlacement(
    bool IsOpen,
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized)
{
    public const double DefaultWidth = 480;
    public const double DefaultHeight = 360;
    public const double MinimumWidth = 320;
    public const double MinimumHeight = 240;

    [JsonIgnore]
    public bool IsUsable =>
        double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height) &&
        Width >= MinimumWidth && Height >= MinimumHeight;
}
