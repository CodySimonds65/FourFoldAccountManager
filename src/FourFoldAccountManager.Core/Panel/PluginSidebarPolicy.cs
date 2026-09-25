namespace FourFoldAccountManager.Core.Panel;

public static class PluginSidebarPolicy
{
    public static bool ShouldShow(
        bool isFullScreen,
        bool expanded,
        Guid? selectedAccountId,
        IReadOnlyCollection<Guid> openAccountIds) =>
        expanded && !isFullScreen && (selectedAccountId.HasValue || openAccountIds.Count > 0);
}
