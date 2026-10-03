namespace FourFoldAccountManager.Desktop.Views;

public sealed record PluginCardRowData(string Label, string Value, double? Progress)
{
    public bool HasProgress => Progress is not null;
}

// A community plugin's card, drawn by FourFold: the card's name, then rows of label and value. IsStale dims the card
// while its plugin isn't running.
public sealed record PluginCardData(
    string Title,
    string AccountLabel,
    bool IsStale,
    IReadOnlyList<PluginCardRowData> Rows,
    string Summary) : IOverlayCardData
{
    public bool HasAccountLabel => AccountLabel.Length > 0;

    public bool IsEmpty => Rows.Count == 0;
}
