namespace FourFoldAccountManager.Core.Panel;

public static class PluginSidebarPolicy
{
    public static bool ShouldShow(
        bool isFullScreen,
        Guid? selectedAccountId,
        IReadOnlyCollection<Guid> openAccountIds) =>
        !isFullScreen && (selectedAccountId.HasValue || openAccountIds.Count > 0);
}
