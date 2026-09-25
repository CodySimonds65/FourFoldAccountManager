namespace FourFoldAccountManager.Core.Models;

public sealed record OverlayBounds(double X, double Y, double Width, double Height)
{
    public bool IsValid =>
        double.IsFinite(X) && double.IsFinite(Y) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        X >= 0 && Y >= 0 && Width > 0 && Height > 0 &&
        Width <= 1 && Height <= 1 && X + Width <= 1 && Y + Height <= 1;

    public OverlayBounds ClampToViewport()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) ||
            !double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0)
        {
            throw new ArgumentException("Overlay bounds must be finite and have positive dimensions.");
        }

        var width = Math.Min(Width, 1d);
        var height = Math.Min(Height, 1d);
        var x = Math.Max(0d, Math.Min(X, 1d - width));
        var y = Math.Max(0d, Math.Min(Y, 1d - height));

        // Belt and suspenders: two independent divisions/subtractions can each round in a way
        // that leaves x + width (or y + height) a hair above 1, even though x and y were just
        // clamped against 1 - width/height. Shrink instead of ever handing back an invalid result.
        if (x + width > 1d)
        {
            width = 1d - x;
        }

        if (y + height > 1d)
        {
            height = 1d - y;
        }

        return new OverlayBounds(x, y, width, height);
    }
}
