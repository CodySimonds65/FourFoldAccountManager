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

Two-profile separation during one run is confirmed by the user-provided screenshot. The subsequent restart check showed all profiles logged out, so persistent sign-in is **not verified and currently fails acceptance**. The probe source uses a fixed local data root and four stable named profiles, but the status log was not refreshed for the user's reopened run; the exact executable and profile path used in that run still need confirmation. A diagnostic probe build now records the effective executable/data/profile identity without reading browser data. No credentials or cookie/token contents were collected. Do not claim restart persistence until a run with verified executable/profile identity succeeds.

The compatibility probe is not sufficient to release the account manager while restart persistence remains a primary requirement. Continue with independent metadata and layout settings; defer the final browser-session architecture and account/panel UI until the probe launch identity and FourFold session behavior are understood.

## Current navigation scope

The planned manager should allow only HTTPS navigation to `fourfoldonline.com` until the manual sign-in flow establishes a need for another origin. This blocks unrelated external links from leaving an account's browser profile. The probe profile data remains under `%LOCALAPPDATA%\FourFoldAccountManager\Probe` and is separate from the eventual account profiles.
