namespace FourFoldAccountManager.Desktop.Services;

/// <summary>
/// Decisions for the slot "copy in-game password" action. The game stopped persisting the in-game
/// password (it used to sit in PlayerPrefs as plaintext), so the manager copies it from the secure
/// credential store on request instead. Native clipboard and credential I/O stay in the window; only
/// these choices live here so they can be tested without WPF.
/// </summary>
public static class InGamePasswordCopy
{
    public enum Readiness
    {
        Ready,
        NoAccount,
        NoPassword
    }

    public static Readiness Classify(Guid? accountId, AccountCredentials? credentials) =>
        accountId is null ? Readiness.NoAccount
        : credentials is null || string.IsNullOrEmpty(credentials.Password) ? Readiness.NoPassword
        : Readiness.Ready;

    /// <summary>
    /// True only when the clipboard still holds exactly what we copied, so the auto-clear never wipes
    /// content the user copied afterwards.
    /// </summary>
    public static bool StillOurClipboard(string? clipboardNow, string copied) =>
        clipboardNow is not null && string.Equals(clipboardNow, copied, StringComparison.Ordinal);
}
