namespace FourFoldAccountManager.Desktop.Views;

internal static class OverlayCardText
{
    // "Alice · Mage 42 · stale": empty parts are left out.
    public static string Header(string accountLabel, string detail, bool isStale) =>
        string.Join(" · ", new[] { accountLabel, detail, isStale ? "stale" : string.Empty }
            .Where(part => part.Length > 0));
}
