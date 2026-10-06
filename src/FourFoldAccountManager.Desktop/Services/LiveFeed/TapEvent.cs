namespace FourFoldAccountManager.Desktop.Services.LiveFeed;

internal enum TapEventKind
{
    SocketCreated,
    Frame,
    SocketClosed,
    ViewClosed
}

// One DevTools event as the tap saw it. Json is the raw parameter object and may hold game traffic: it's parsed on the
// feed's worker and never logged or kept.
internal readonly record struct TapEvent(Guid AccountId, TapEventKind Kind, string Json, DateTimeOffset At)
{
    // Json is never printed: it may hold game traffic.
    public override string ToString() => $"TapEvent {{ AccountId = {AccountId}, Kind = {Kind}, At = {At:O} }}";
}
