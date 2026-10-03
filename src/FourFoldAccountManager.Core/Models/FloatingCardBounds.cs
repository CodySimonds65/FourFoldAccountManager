using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// A floating card window's spot on the desktop, in WPF device-independent units. Left and Top may be negative on
// a monitor left of or above the main one.
public sealed record FloatingCardBounds(double Left, double Top, double Width, double Height)
{
    [JsonIgnore]
    public bool IsUsable =>
        double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height) &&
        Width > 0 && Height > 0;
}
