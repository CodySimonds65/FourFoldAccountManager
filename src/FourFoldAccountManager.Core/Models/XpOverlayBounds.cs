namespace FourFoldAccountManager.Core.Models;

public sealed record XpOverlayBounds(double X, double Y, double Width, double Height)
{
    public bool IsValid =>
        double.IsFinite(X) && double.IsFinite(Y) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        X >= 0 && Y >= 0 && Width > 0 && Height > 0 &&
        Width <= 1 && Height <= 1 && X + Width <= 1 && Y + Height <= 1;

    public XpOverlayBounds ClampToViewport()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) ||
            !double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0)
        {
            throw new ArgumentException("Overlay bounds must be finite and have positive dimensions.");
        }

        var width = Math.Min(Width, 1d);
        var height = Math.Min(Height, 1d);
        return new XpOverlayBounds(
            Math.Clamp(X, 0d, 1d - width),
            Math.Clamp(Y, 0d, 1d - height),
            width,
            height);
    }
}
