using System.Globalization;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record XpCalcCardData(string AccountLabel, bool IsStale, XpCalcCardContent Content) : IOverlayCardData
{
    public string HeaderText => OverlayCardText.Header(AccountLabel, Content.TargetLine, IsStale);

    public string LineText => Content.IsAvailable ? Content.RemainingLine : Content.Status;

    public string Summary => Content.TargetLevel is { } target
        ? string.Create(CultureInfo.InvariantCulture, $"→ {target}")
        : string.Empty;
}
