# FourFold WebView2 Compatibility

## Probe setup

- Windows WPF probe built with .NET 10 and Microsoft.Web.WebView2 1.0.4078.44.
- Four `WebView2` controls shared one environment and used four distinct stable profile names.
- Start URL: `https://fourfoldonline.com/play.php`.
- Probe status output records the executable path, effective WebView2 data-folder path, profile names, InPrivate flags, profile numbers, origin, navigation result, and exception type. It does not record page content, credentials, cookies, tokens, or full URLs.

## Observed

- All four profiles initialized in one WPF window.
- All four top-level navigations to the official `https://fourfoldonline.com` origin completed successfully in the guest state.
- The probe window remained responsive at its 1440×900 default size.
- A user-provided probe screenshot shows two distinct accounts signed in simultaneously in separate profiles; the account names are not retained in the project.
- After the user closed and reopened the probe, a user-provided screenshot showed all four profiles in the guest state. Persistence across app restart has therefore failed in the observed run.
- No popup or cross-origin top-level navigation occurred during the guest-page load.
- The official site links its play and sign-in routes on the same apex origin. The current guest flow exposes `/play.php` and `/login.php`; sign-in submission and post-login gameplay have not yet been exercised inside WebView2.

## Compatibility status

Two-profile separation during one run is confirmed by the user-provided screenshot. After reopening, the user reported that no profiles remained signed in. A diagnostic run confirmed the probe executable path, writable `%LOCALAPPDATA%\FourFoldAccountManager\Probe` user-data folder, the same four stable runtime profile names, and `InPrivate=false`. This rules out an InPrivate profile or an unwritable data root in that run, but does not reveal FourFold's authentication cookie or server policy. Persistent sign-in therefore **fails the observed acceptance check**; FourFold ending authentication at browser shutdown is the leading explanation, not a proven cause. The manager's global **Launch accounts** action can submit saved credentials again when opening the account profiles; users can also sign in manually when a profile has no saved login. No credentials, page contents, cookie values, or authentication tokens were collected. Do not claim restart persistence or silently replace this requirement with repeated manual sign-in until the user accepts the limitation.

The main app can retain its local browser profile data, but FourFold authentication may not survive closing the app. The manual post-login redirect and authenticated gameplay flow have not yet been independently verified in the shipped app. Keep restart persistence listed as unresolved until the user accepts the manual re-sign-in fallback or FourFold's behavior proves persistent.

## Current navigation scope

The planned manager should allow only HTTPS navigation to `fourfoldonline.com` until the manual sign-in flow establishes a need for another origin. This blocks unrelated external links from leaving an account's browser profile. The probe profile data remains under `%LOCALAPPDATA%\FourFoldAccountManager\Probe` and is separate from the eventual account profiles.
