namespace FourFoldAccountManager.Desktop.Services;

public static class FourFoldDestination
{
    public static Uri StartUri { get; } = new("https://fourfoldonline.com/play.php");

    public static Uri LoginUri { get; } = new("https://fourfoldonline.com/login.php");

    public static Uri LoginSubmitUri { get; } = new("https://fourfoldonline.com/auth.php");
}
