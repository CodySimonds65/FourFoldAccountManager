namespace FourFoldAccountManager.Core.Launch;

public static class AssignedAccountLaunchPolicy
{
    public static bool ShouldStart(bool isOpen, bool hasFailure) =>
        !isOpen || hasFailure;

    public static bool ShouldShowRelaunch(bool isAssigned, bool isOpen) =>
        isAssigned && isOpen;
}
