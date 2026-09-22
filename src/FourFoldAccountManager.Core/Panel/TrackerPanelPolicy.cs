namespace FourFoldAccountManager.Core.Panel;

public static class TrackerPanelPolicy
{
    public static bool ShouldShow(bool isFullScreen, IReadOnlyCollection<Guid> openAccountIds) =>
        !isFullScreen && openAccountIds.Count > 0;
}
