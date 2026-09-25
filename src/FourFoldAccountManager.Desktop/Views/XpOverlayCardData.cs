namespace FourFoldAccountManager.Desktop.Views;

public sealed record XpOverlayCardData(string AccountLabel, string XpPerHourText) : IOverlayCardData
{
    public string Summary => XpPerHourText;
}
