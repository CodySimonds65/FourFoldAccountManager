namespace FourFoldAccountManager.Core.Launch;

public static class AssignedAccountLaunchPolicy
{
    public static bool ShouldStart(bool isOpen, bool hasFailure) =>
        !isOpen || hasFailure;
}
