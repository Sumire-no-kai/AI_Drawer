# Privacy

## What AI Drawer is

AI Drawer is a native Windows shell around third-party provider websites. It has no AI backend, AI Drawer account, API-key service, analytics backend, cloud synchronization, or in-app payment system.

## Data AI Drawer native code may store

- application settings, window state, and shortcut preferences;
- provider identifiers and compatibility labels;
- native workspace identifiers, labels, order, lifecycle state, and Keep active preferences;
- local onboarding and optional support-reminder state;
- permission decisions and coarse process-health information;
- for reviewed providers only, one current-user-encrypted restricted restore locator containing an exact HTTPS provider origin and allowlisted conversation path with query and fragment removed.

## Data AI Drawer native code does not collect

AI Drawer does not read or store prompts, AI responses, conversation history, page DOM, credentials, passwords, cookies, tokens, payment information, checkout identifiers, uploaded-file contents, full sensitive URLs, OAuth codes, or provider network traces.

## WebView2 website data

Provider websites run inside Microsoft Edge WebView2. WebView2 may store cookies, cache, site storage, and authenticated sessions in an AI Drawer-specific local user-data folder. AI Drawer native code does not inspect those cookies or tokens. This profile is separate from the user's normal Edge or Chrome profile, but anyone who can access the Windows account may also be able to use its signed-in provider sessions.

The application separates `Clear provider cache` from `Reset provider website data`. Cache clearing targets temporary web resources and is intended to preserve sign-in. Website-data reset removes that provider profile's cookies, cache, site storage, and remembered permissions, and therefore signs every workspace using that provider profile out locally. It does not delete the provider account or provider-hosted history.

## External services

When the user chooses an external link, support page, feedback form, security report, or browser recovery action, Windows opens a fixed or sanitized HTTPS destination in the system browser. AI Drawer does not append prompts, account identifiers, provider page content, cookies, tokens, or application diagnostics to the Microsoft Forms or Buy Me a Coffee links.
