namespace FourFoldAccountManager.Desktop.Views;

internal sealed class FullscreenXpOverlayTabVisibilityState
{
    private bool _dismissed;

    public bool IsVisible(bool isFullScreen) => isFullScreen && !_dismissed;

    public void Dismiss() => _dismissed = true;

    public void Reveal() => _dismissed = false;
}
