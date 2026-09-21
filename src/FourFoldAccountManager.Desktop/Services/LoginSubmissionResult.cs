namespace FourFoldAccountManager.Desktop.Services;

public enum LoginSubmissionResult
{
    Submitted,
    ViewNotOpen,
    NotOnLoginPage,
    LoginFieldsNotFound,
    NavigationFailed,
    TimedOut
}
