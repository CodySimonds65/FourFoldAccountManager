using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record StatsCardData(string AccountLabel, bool IsStale, StatsCardContent Content) : IOverlayCardData
{
    public string HeaderText => OverlayCardText.Header(AccountLabel, Content.Header, IsStale);

    public string LineText => Content.IsAvailable ? Content.SummaryText : Content.Status;

    public IReadOnlyList<StatsCardRow> Rows => Content.Rows;

    public string Summary => Content.CountsText;
}
