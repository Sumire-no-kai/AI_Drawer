# AI Drawer threat model

## Scope and assets

AI Drawer is a native Windows shell around provider-owned WebView2 pages. The sensitive assets are provider sessions and browser-origin data managed by WebView2, native settings, encrypted restricted restore locators, and the user's trust in navigation, permissions, downloads, and release artifacts.

The native shell must not read or store prompts, responses, page DOM, credentials, cookies, tokens, request bodies, or payment data.

## Trust boundaries and controls

| Boundary | Primary threat | Implemented control | Remaining evidence |
| --- | --- | --- | --- |
| Provider page to native shell | Page obtains a native bridge or developer tools access | Host objects, web messages, DevTools, password autosave, and general autofill are disabled through one shared configurator | Runtime inspection on a packaged build |
| Navigation and popups | Origin confusion, credentials in URLs, custom ports, certificate bypass, or unreviewed browsing | Exact HTTPS origin classification, no user info or custom port, safe external handoff, certificate errors cancelled, and controlled popups | Provider-specific runtime matrix |
| Commerce | Checkout or payment happens inside the shell | Reviewed purchase routes are cancelled and explained natively; no payment collection or handoff exists | Provider route discovery and runtime coverage |
| Provider profiles | Native app leaks, imports, or over-clears website data | App-specific WebView2 root; reset scope disclosed; no Edge/Chrome profile import; no DOM/cookie APIs | Signed-profile cache/reset tests |
| Download | Overwrite, traversal, dangerous name, or silent execution | Filename sanitization, collision-free destination, confirmation, risk label, no auto-open | Real-provider download flow |
| Process failure | Reload loop, data loss, or broken recovery | Bounded renderer reload, same-profile restart, browser-environment recreation, OOM release of inactive views | Controlled renderer/browser failure run |
| Session persistence | Sensitive URL persistence or corrupt-session overwrite | Current-user-encrypted restrictive locators, query/fragment removal, validation on save/load, blocked-session backup | Multiple Windows user and packaged upgrade tests |
| Release artifacts | Unsigned or altered artifact is published | Internal candidate rejects signatures/private key material and records SHA-256/source metadata | Signing/provenance design and signed-candidate validation |

## Out of scope

Provider authentication rules, account recovery, provider-hosted conversation content, and provider payment processing remain provider responsibilities. AI Drawer must not bypass embedded-browser restrictions or inspect provider content to emulate missing functionality.

## Reporting

Do not place sensitive security details in public issues. `SECURITY.md` is not yet a private reporting channel; Public Beta remains blocked until a reviewed private channel is enabled and tested.
