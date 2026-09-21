namespace FourFoldAccountManager.Desktop.Services;

public enum NavigationBlockedKind
{
    RequestedNavigation,
    TopLevelNavigation,
    Popup
}

public sealed class NavigationBlockedEventArgs(Guid accountId, NavigationBlockedKind kind) : EventArgs
{
    public Guid AccountId { get; } = accountId;

    public NavigationBlockedKind Kind { get; } = kind;
}
