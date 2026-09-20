# FourFold WebView2 Compatibility

## Probe setup

- Windows WPF probe built with .NET 10 and Microsoft.Web.WebView2 1.0.4078.44.
- Four `WebView2` controls shared one environment and used four distinct stable profile names.
- Start URL: `https://fourfoldonline.com/play.php`.
- Probe status output records only profile numbers, origin, navigation result, and exception type. It does not record page content, credentials, cookies, or full URLs.

## Observed

- All four profiles initialized in one WPF window.
- All four top-level navigations to the official `https://fourfoldonline.com` origin completed successfully in the guest state.
- The probe window remained responsive at its 1440×900 default size.
- A user-provided probe screenshot shows two distinct accounts signed in simultaneously in separate profiles; the account names are not retained in the project.
- No popup or cross-origin top-level navigation occurred during the guest-page load.
- The official site links its play and sign-in routes on the same apex origin. The current guest flow exposes `/play.php` and `/login.php`; sign-in submission and post-login gameplay have not yet been exercised inside WebView2.

## Manual verification still required

The two-profile separation check is confirmed by the user-provided screenshot. Persistence across closing and reopening the probe remains pending. The user has been asked to restart it manually because this session has no Windows native-app controls. No credentials were collected by the manager or written to the probe log. If sign-in introduces another origin or a required popup, update the allow-list and popup behavior based on that observed flow before release.

## Current navigation scope

The planned manager should allow only HTTPS navigation to `fourfoldonline.com` until the manual sign-in flow establishes a need for another origin. This blocks unrelated external links from leaving an account's browser profile. The probe profile data remains under `%LOCALAPPDATA%\FourFoldAccountManager\Probe` and is separate from the eventual account profiles.
