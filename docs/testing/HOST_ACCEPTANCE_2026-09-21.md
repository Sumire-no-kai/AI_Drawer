# Windows 11 x64 retained-tab acceptance — 2026-09-21

This record covers the retained conversation-tab and split-view redesign without using any provider account. It is evidence for one Windows 11 x64 development host. It is not account compatibility, signed-package, Windows 10, or ARM64-device evidence.

## Target and host

- Production source baseline: `a7d9b5e` on `fix/no-account-acceptance`.
- Host: Windows 11 25H2 `10.0.26200.9457`, x64, 2560×1440 at 150% display scale.
- WebView2 Runtime: `153.0.4234.48`.
- SDK: repository-local .NET SDK `10.0.400`.
- Test profiles: generated isolated application-data and WebView2 directories only. Existing browser and AI-provider profiles were not opened or modified.

## Build and automated checks

- Core Release policy harness: 47/47 checks passed.
- Application Debug harness: 23/23 non-UI checks and 27/27 complete UI Automation checks passed. The final UI suite used only its generated isolated data root.
- Production x64 Debug and Release builds passed with zero warnings and errors; the Release build treated warnings as errors.
- Production-app and application-test projects passed `dotnet format --verify-no-changes --no-restore`.
- The application-test executable is intentionally run in Debug because its isolated data-root hook is Debug-only. Attempting to start the Release test executable unpackaged stops at Windows App SDK package-identity initialization; the production Release build passed separately.

## Retained-page runtime acceptance

The runtime harness used `https://example.com/` in generated profiles. It did not use provider accounts, authentication, normal application data, prompts, responses, DOM access, credentials, cookies, tokens, payment data, or provider network traces.

- Exact-final-code fast run: 20/20 checks passed. Report SHA-256: `14D50556130E4A11DD6A9376226B9C90F7FBD27C63C78B25A6C9E3F3C7667656`. It covered cold start, single-instance redirection, five retained pages, same-provider `Ctrl+T`, rename, physical drag ordering, search, positional and cycle shortcuts, explicit release and recreation, cache and data reset APIs, Renderer/GPU/Browser recovery, tray and shortcut restoration, second launch, exact Exit, and zero process/profile residue.
- Exact-final-code five-minute run: 21/21 checks passed. Report SHA-256: `AD05A3066D608B74FD93A5C69D9632BFE82EC97E31E17276BBDE7B28B7FB73E5`.
- Across the five-minute interval, five retained pages stayed live and the WebView2 process count remained 10. WebView2 working set changed from `425996288` to `492969984` bytes (about 406.3 to 470.1 MiB). This is one interval on one host; it does not prove the absence of a long-term memory leak.
- The prior native drag implementation did not receive movement reliably inside the horizontally scrolling tab strip. Page-level pointer tracking with an 8-pixel threshold now performs the reorder, dims the dragged tab, accepts a drop only within the tab-strip height, and persists the resulting order. The final runtime run physically dragged the fifth tab left and verified the saved provider identity and order.

## Real provider guest observations

These observations used fresh isolated profiles and visual interaction only. No existing account was used, no sign-in or sign-out action was taken, and no page DOM, cookies, tokens, or network traces were inspected.

| Provider | Guest observation | Evidence boundary |
| --- | --- | --- |
| ChatGPT | Guest page loaded, accepted a test message, completed a response, and preserved the rendered conversation after switching away, returning, entering split view with Gemini, narrowing and widening the window, and leaving split view. | One anonymous session on this host. Upload, download, permissions, popups, purchase routes, history, and restart persistence were not exercised. |
| Gemini | Guest page loaded and accepted a test message submission. It later presented a sign-in card instead of a completed guest response. The page remained present while switching and in split view. | This does not establish complete anonymous conversation support. Account and history behavior were not exercised. |
| Claude | Fresh profile opened the provider sign-in page. No login action was taken. A third-party frame produced the app's native security warning, and the new dismiss action removed the warning without altering the provider page. | Anonymous conversation was unavailable in this observation. Authentication and post-login behavior remain untested. |

Provider status labels remain unchanged. These guest observations do not promote any provider to `Verified`.

## UI and accessibility review

- At the host's 150% scale, the former 1000-DIP split threshold made the default window silently show one pane. The threshold is now 720 DIPs, so the default window shows both panes. When the window is narrower, the retained split state remains explicit in the pane header and resumes after widening without rebuilding either page.
- Selecting or creating a tab now scrolls it into view. This fixes the fifth tab being focused and persisted while remaining unreachable in the hidden-overflow strip.
- Native status warnings now have a keyboard-addressable dismiss button. This keeps fail-closed provider warnings visible while allowing the user to recover the page area after reading them.
- Dynamic confirmation buttons expose their actual action names to UI Automation. The release-page prompt was completed through keyboard focus in runtime acceptance.
- Tab management, the workspace action menu, release confirmation, narrow/wide split behavior, and guest-provider layouts were visually inspected at 150%. No blocking overlap, clipped core action, ambiguous focused pane, or unreadable prompt was found after the fixes.

## Remaining gates

- Provider-account login, shared-session behavior, authentication popups, history, upload/download, permission decisions, external references, purchase boundaries, signed-in cache/reset behavior, and account recovery.
- Windows 10 x64 and Windows 11 ARM64 matching-device runtime checks.
- Live High Contrast, Narrator, 200% display DPI, longer endurance/resource observation, and human assistive-technology review.
- Clean signed-package install, update, startup registration, rollback, uninstall, retained-data behavior, and exact public bytes.
