# AI Dock Development Log

This is the durable development record for implementation decisions, verified behavior, limitations, and open work. It is not a release changelog. Planned behavior must not be presented as shipped or provider-compatible behavior.

## 2026-08-21 — M2.2 workspace persistence and lifecycle implementation

### Included code

- Separated native conversation-workspace identity from disposable live WebViews. Workspace identity, order, provider assignment, selected workspace, Keep active preference, and an optional protected restore locator now have a versioned local session document.
- Added a provisional Balanced lifecycle: inactive views request low memory and receive a five-minute grace period; steady state targets two live views; temporary overflow is capped at three; opening beyond the hard cap disposes an inactive view without deleting its native tab. Tabs expose recent and reload-required states.
- Added a provider-specific restore-locator policy. It accepts only the exact HTTPS provider host and an allowlisted opaque conversation path, strips query and fragment values, rejects user-info/custom-port/subdomain/authentication/unknown-path forms, encrypts the locator for the current Windows user, and revalidates it after decryption. ChatGPT `/c/`, Claude `/chat/`, and Gemini `/app/` are provisional rules; all other providers fail closed to provider home.
- Added versioned first-run onboarding and Settings surfaces for exact restoration, the future memory modes, About & Support, and replaying the welcome/privacy explanation. Low Memory and Fast Switching remain disabled pending measured Windows 10/11 budgets.
- Added Keep active per workspace, tray access to Settings, provider-wide restore-locator clearing during website-data reset, and an affected-workspace count in the reset confirmation.
- Added the local-only BMC eligibility state required by the PRD: never before day 7; eligible after day 14 or 20 successful workspace openings; 90-day Not now; permanent Don't ask again. The disclosed destination is Edward Lee's shared independent-project support page, and the UI states that contributions do not activate applications, provider services, subscriptions, accounts, premium features, or support plans.
- Added a framework-free policy-check project for locator rejection and lifecycle selection, plus `CONTEXT.md` and ADR 0001 describing the domain boundary and accepted privacy decision.

### Verification boundary

- The x64 Debug application build completed with zero warnings and zero errors. The framework-free policy harness completed all six locator rejection and lifecycle-selection checks.
- The executable was launched directly from the repository's unpackaged Debug output. No AI Drawer package was installed or registered and no Start-menu entry was created. Settings open/close and welcome replay were exercised through the running WinUI application.
- Both welcome actions, `Continue` and `Skip`, were verified through their actual accessible button bounds. An initial failure was traced to the shared Composition transition overwriting XAML-owned `Visual.Offset`, which rendered the welcome card away from its hit-test layout. The transition now fades without changing layout coordinates; the same unsafe offset animation was removed from other workspace surfaces.
- Fresh diff review also hardened corrupt-session handling: duplicate workspace IDs are ignored, and an unavailable provider definition preserves the workspace's provider identity and exposes a warning instead of silently converting it to a blank workspace.
- Compilation, XAML loading, the focused welcome/Settings flow, and policy checks are verified. DPAPI round-trip with a real provider locator, full restart restoration, provider URL behavior, provider-profile reset, BMC link launch and eligibility timing, keyboard/focus/accessibility breadth, Windows 10/11 coverage, and instrumented bounded process counts remain open before M2 can pass.
- No provider compatibility status changed. The provisional locator path rules are not compatibility claims and must be removed or revised if provider-specific verification fails.
- The configured Buy Me a Coffee destination is `https://buymeacoffee.com/edward_lee`. The Settings disclosure rendered in the focused smoke run; external link launch, eligibility timing, snooze, and permanent dismissal remain unverified.

## 2026-08-20 — M2.1 workspace-home design alignment

### Included behavior

- Replaced the bordered provider table with a centered workspace home, restrained ambient light, and one floating two-column provider list. Provider compatibility labels remain visible without presenting any provider as verified.
- Added the selected `Sixfold Pulse` brand mark. The home and packaged Windows assets use the same bright-indigo silhouette on a transparent background.
- Added short state-driven transitions for workspace creation, workspace close, home/WebView switching, status presentation, recovery, and in-app confirmations. The implementation respects the Windows animation preference.
- Replaced application-owned `InfoBar`, `ContentDialog`, and menu presentation with a consistent in-app status banner, confirmation layer, and action surface. Operating-system security surfaces and notification-area behavior remain system-owned.
- Numbered simultaneous blank workspaces, bounded tab width, and removed the row-reorder transition that could temporarily overlap tab labels.
- Kept the native workspace bar above provider content. Initial provider startup now uses a branded pulse surface, while later provider navigation uses a non-blocking activity line instead of replacing the workspace shell.
- Added a notification-area right-click menu wired to `Open AI Drawer` and `Exit AI Drawer`. Settings remains omitted until a real settings surface exists.

### Verification and limitations

- `D:\DevTools\dotnet\dotnet.exe build src\AIDrawer.App\AIDrawer.App.csproj -c Debug -p:Platform=x64 --no-restore` completed with zero warnings and zero errors.
- Generated brand assets were visually inspected at application-icon resolution. The home SVG and package icon share the same selected silhouette.
- Provider login, prompt submission, permission decisions, data reset, purchase routes, and conversation content were not exercised during this design pass. No provider compatibility state changed.
- Focused manual checks of the final centered layout, repeated blank-workspace creation/close animation, custom confirmation focus behavior, tray-menu invocation, and light/dark themes remain required. No additional package was registered for this final review.

## 2026-08-20 — M2 multi-provider workspace foundation

### Scope and implementation boundary

- Replaced the single hard-coded Gemini workspace with a data-driven registry containing the eight provider entries that have at least initial M0 observations. Their existing `Experimental` or `Limited` states remain visible; this change does not add any `Verified` provider claim.
- The native application still does not inspect provider DOM, prompts, responses, credentials, cookies, tokens, or payment information.

### Included behavior

1. **Provider selection.** The compact selector lists the configured providers and displays each current compatibility state. `Ctrl + 1` through `Ctrl + 8` select the entries in selector order.
2. **Isolated, persistent workspaces.** All WebViews use one AI Drawer-owned user-data root with stable `provider-<id>` profiles. The application does not reuse normal Edge or Chrome data.
3. **Bounded lifecycle.** A workspace is created only when selected. The visible workspace targets normal memory; a hidden or warm workspace targets low memory. Selecting a third workspace disposes the least recently used WebView while preserving its profile, so the implementation retains at most two live views.
4. **Native boundaries.** Unknown or non-HTTPS navigation is blocked, unrelated HTTPS links are handed to the system browser without query or fragment values, and known Gemini purchase paths remain blocked. Permission requests are denied unless the user explicitly chooses Allow in a native prompt; remembered decisions require a separate checkbox.
5. **Reset and recovery.** The overflow menu includes an explicitly confirmed per-workspace website-data reset. It closes the affected view before WebView2 clears that profile's local website data, then recreates only that workspace. Reload and restart continue to preserve the profile.

### Verification and limitations

- `D:\DevTools\dotnet\dotnet.exe build src\AIDrawer.App\AIDrawer.App.csproj --configuration Debug --arch x64 --no-restore` completed with zero warnings and zero errors.
- A locally registered Debug package opened to the Gemini signed-out page. The provider selector listed all eight entries, selection opened ChatGPT, and `Ctrl + 2` opened ChatGPT after Gemini was made `Ctrl + 1`. No provider login, prompt submission, permission action, reset action, purchase action, or external-link action was performed.
- Review found and fixed inactive-workspace state updates affecting the selected UI, reset leaving an active workspace without reinitialization, environment-creation failures escaping the recoverable UI path, and concurrent selection/restart/reset operations.
- Real account flows, popup/OAuth paths, permission dialogs, external-browser handoff, website-data reset, renderer failure, hidden-window behavior, resource measurements, and the two-view limit under an instrumented process check remain focused manual-test work. Existing M1 tester profiles are not migrated into the new multi-profile layout; the prior development profile had already been cleared.

## 2026-08-20 — M1 Windows shell foundation

### Scope and implementation boundary

- Added the first formal application project at `src/AIDrawer.App`, using C#, .NET 10, WinUI 3, Windows App SDK, WebView2, and MSIX development tooling.
- The shell currently hosts one Gemini workspace only. Gemini remains `Experimental`; this does not change the M0 compatibility result or claim public support.
- The application code does not inspect provider DOM, prompts, responses, credentials, cookies, tokens, or payment information.

### Included behavior

1. **Application lifecycle.** A custom startup entry point coordinates a single packaged application instance. A second launch redirects activation to the primary instance.
2. **Minimal native shell.** The window uses a compact workspace bar, an in-page Gemini host, reload/restart controls, recoverable status messaging, and a true-exit action. Closing the window hides it while the process remains available.
3. **Local WebView profile.** Gemini uses an application-specific profile under local application data. It is isolated from normal Edge and Chrome profiles. The development test profile was cleared after implementation; no test login state remains.
4. **Subscription boundary.** The initial Gemini policy cancels only reviewed Google payment hosts and Gemini purchase-path fragments, then shows a native explanation. It is not a guarantee that every future provider route is covered.
5. **Shortcut and tray foundation.** The shell registers the proposed `Win + Shift + A` shortcut through Win32 and creates one notification-area icon through H.NotifyIcon.WinUI. Direct `WM_HOTKEY` handling was exercised during development; the physical key chord still needs a user-operated verification before it can be considered reliable.
6. **Failure recovery.** WebView initialization, navigation, and process-failure states give the user reload or same-profile restart actions without reading page content.

### Verification and limitations

- `dotnet build src/AIDrawer.App/AIDrawer.App.csproj --configuration Debug --arch x64 --no-restore` completed with zero warnings and zero errors.
- Debug packaged launch, one-instance activation, and close-to-hide restoration were exercised locally. Before the local profile was cleared, repeated shell launches could load an existing Gemini session; no programmatic sign-in, prompt submission, upload, purchase, or account-setting action was performed. All M0 and Debug WebView2 profiles were then removed, and future automated checks use an empty profile only.
- The current scope does not yet include provider selection, multi-workspace lifecycle limits, settings/profile reset UI, general external-navigation policy, full tray context menu, packaged release validation, or physical shortcut verification.

## 2026-08-20 — Initial multi-provider manual sweep

### Scope and evidence

- One manually operated sweep covered Grok, ChatGPT, Claude, DeepSeek, Doubao, Qwen Studio, and GLM using persistent provider profiles and WebView2 Runtime `151.0.4129.93`.
- Evidence consists of the user's direct observations and a sanitized in-memory event log containing event categories and origins only. The exact repository commit, Windows build, account classes, region, prompt text, and response text were not recorded for this sweep.
- The results are environment-scoped and incomplete. None of these providers becomes `Verified` from this run.

### Recorded outcomes

1. **Grok is `Limited`, with root cause open.** X sign-in returned successfully and the same-profile WebView recreation retained login. Conversation submission completed and user-initiated copy returned the reply text, but the reply itself rendered blank. A normal Edge or Chrome comparison is still required before attributing this to WebView2. The Grok purchase page remained reachable.
2. **ChatGPT is `Experimental`.** Initial sign-in and basic use were reported normal, including navigation through Google authentication origins. Its purchase page remained reachable, so the tested purchase boundary failed and requires narrow route discovery.
3. **Claude is `Experimental`.** Google sign-in opened a separate small popup and returned successfully. Plan content is present inside the provider page. AI Drawer will not inspect or alter the DOM to hide it; the actual checkout transition remains untested.
4. **DeepSeek, Doubao, Qwen Studio, and GLM are `Experimental`.** Initial page, sign-in where exercised, and basic use were reported normal, but required feature, persistence, recovery, resource, and purchase cases remain incomplete.
5. **Do not equate a subscription panel with completed checkout.** Doubao's informational subscription modal remains visible. The native boundary should act when a reviewed checkout, payment, recharge, or billing transition is attempted, not by manipulating provider-owned page content.

### Regional-entry decisions

- Qwen Studio (`chat.qwen.ai`) and Tongyi Qianwen (`tongyi.aliyun.com/qianwen`) are separate international and China candidates.
- GLM / 智谱清言 (`chatglm.cn`) and Z.ai (`chat.z.ai`) are separate China and international candidates.
- Each regional candidate receives an independent persistent profile and compatibility result. Evidence and login state do not transfer between regional sites.
- Existing profile IDs `doubao`, `qwen`, and `glm` are preserved so the tester's local D-drive sessions are not orphaned. Only the new regional candidates receive new IDs.
- An international Doubao-family entry remains open. Cici currently redirects toward Dola, so brand relationship, regional availability, and the stable official chat entry must be reviewed before hard-coding a candidate.

### Navigation observations

- Grok authentication used sanitized origins including `accounts.x.ai`, `x.com`, `auth.grok.com`, `auth.grokusercontent.com`, `auth.x.ai`, and `auth.grokipedia.com`.
- ChatGPT navigation included `auth.openai.com` and Google account origins. A permission request was logged as numeric kind `13`; its semantic permission name was not inferred.
- Claude requested Google authentication and `claude.com` popups.
- Doubao authentication used `accounts.feishu.cn` and returned to `www.doubao.com`.
- Transient `ConnectionAborted` events during Grok and Doubao redirects were followed by successful navigation and are observations, not standalone compatibility failures.

### Settings data-reset decision

- The production settings page will distinguish cache cleanup from destructive website-data reset. `Clear cache` is intended to preserve sign-in; `Reset website data` clears the selected WebView2 profile's cookies, storage, permissions, and cache and therefore signs that local workspace out.
- Reset scope follows the profile boundary: one workspace first, optionally all workspaces for one provider, and a separately confirmed global reset for all AI Drawer web profiles.
- All affected WebViews must be disposed before reset. Cleanup uses WebView2 profile/data APIs without reading cookies, tokens, page content, prompts, or responses.
- The confirmation text must state that local sign-in is removed but the provider account and provider-hosted conversation history are not deleted. Re-authentication may make that history visible again.
- This is a requirement for the production settings experience, not an M0 Compatibility Lab feature.

## 2026-08-20 — M0 provider-matrix harness foundation

### Scope

- The Gemini-only feasibility harness was extended into a manual test harness for the initial PRD provider matrix: ChatGPT, Claude, Gemini, Grok, DeepSeek, Doubao / 豆包, Qwen / 通义千问, and GLM / 智谱清言.
- This is tooling and documentation work only. No new manual provider result was recorded in this change.

### Implementation decisions

1. **Keep one live WebView.** The lab remains one native window and one WebView at a time; M0 does not implement the production multi-workspace or bounded multi-WebView lifecycle.
2. **Keep candidates data-driven.** Each candidate supplies a display name, official test entry URL, local profile key, compatibility status label, and only reviewed purchase-route rules.
3. **Separate profiles by provider.** Persistent and fresh profiles are nested under a provider-specific directory. A fresh run receives a millisecond timestamp to avoid collisions between successive manual tests.
4. **Do not invent payment policy.** Gemini retains the routes manually observed in the prior run. Other providers display that no known purchase route is configured and require manual verification before a block rule is added.
5. **Permit deliberate switching.** Ending a test closes the current WebView before the tester changes provider or profile, avoiding cross-provider profile reuse in the lab.
6. **Preserve diagnostics and privacy behavior.** Event messages remain sanitized and in memory only. Resource sampling continues to aggregate private memory without reading page content.

### Documentation

- Added a common provider test plan and compatibility matrix with the existing Gemini evidence transcribed as `Experimental`.
- Retained the Gemini-specific plan and original run record as historical evidence.

## 2026-08-20 — Gemini WebView2 Milestone 0 feasibility run

### Scope and repository state

- Goal: determine whether the current Gemini web experience is viable inside a minimal WinUI 3 + WebView2 desktop shell before building the polished MVP.
- Branch: `test/gemini-webview2-feasibility`.
- Base commit: `ec976a5` (`docs: establish project requirements and workflow`).
- Remote `master` was in sync before the branch was created.
- This run covers one Windows machine and one manually operated account session. It is not a universal Gemini support claim.
- The user requested that this work remain local for now. No push or pull request is part of this log entry.

### Environment

| Item | Recorded value |
| --- | --- |
| OS | Windows build `10.0.26200`, x64 |
| .NET SDK | `10.0.400` |
| Windows App SDK package | `2.2.0` |
| WebView2 Runtime | `151.0.4129.93` |
| Build configuration | Debug, x64 |
| Account class | Not recorded; do not infer |
| Region | Not recorded; do not infer |

The machine initially had .NET runtimes but no .NET SDK or Visual Studio installation. The Microsoft .NET CLI route was selected because it is sufficient for this compatibility harness and avoids installing the full IDE.

Developer Mode was enabled only to launch the locally built packaged debug application. This is a development-machine requirement, not a requirement for a future Microsoft Store installation.

### Storage decisions

- .NET SDK: `D:\DevTools\dotnet`.
- .NET CLI state: `D:\DevTools\dotnet-cli-home`.
- NuGet package cache: `D:\DevTools\nuget-packages`.
- Gemini test profiles: `D:\AI Dock TestData\Gemini`.
- Build output remains under the repository on E drive.
- The debug package identity is registered by Microsoft's WinApp CLI; the harness is not installed through a normal end-user installer.
- These are test-machine paths only. A formal release should use normal Windows or Microsoft Store installation and application-data conventions unless a later product decision changes that behavior.

### Implementation decisions

1. **Use the accepted product stack.** The harness uses C#, .NET 10, WinUI 3, Windows App SDK, WebView2, and packaged debug identity.
2. **Keep the harness deliberately small.** It contains one native window, one page, and one WebView2. No MVVM framework, dependency injection, provider abstraction, database, backend, DOM automation, or private provider API was introduced.
3. **Separate persistent and disposable profiles.** `Persistent` reuses a stable user-data folder for session tests. `Fresh disposable` creates a timestamped folder for clean-profile tests.
4. **Keep provider data provider-owned.** The harness does not build a second conversation cache. Gemini history is loaded by Gemini after its account session is restored.
5. **Do not bypass authentication controls.** No User-Agent override, browser-cookie import, token interception, private API, or automated sign-in is used.
6. **Preserve the privacy boundary.** The harness does not inspect the DOM and does not record credentials, cookies, tokens, prompts, responses, request bodies, payment data, or full URLs.
7. **Use sanitized in-memory diagnostics.** Navigation, popup, permission, and process-failure events are shown only in memory. URI text is reduced to scheme, host, and non-default port.
8. **Keep permissions user-controlled.** Permission requests are observed and logged by category, but the harness does not silently grant them.
9. **Block known purchase entry points inside the harness.** Known Google payment/subscription origins and Gemini upgrade/subscription paths are cancelled before internal navigation or popup creation. This is a maintained policy, not a permanent guarantee against provider URL changes.
10. **Measure WebView resources without reading page content.** Process count, approximate CPU, and aggregate private memory are sampled from WebView2 process information.
11. **Correct the initial memory metric.** The first build summed per-process working sets, which can double-count shared pages. After the test it was changed to aggregate `PrivateMemorySize64`. The original `702.8 MB` screenshot observation is retained below but is invalid for product memory claims.

### Build evidence

The harness built successfully before the manual run:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

The app then launched successfully through the Windows App Development CLI after Developer Mode was enabled.

### Manual test results

Evidence comes from the user's direct interaction and a screenshot of the signed-out harness. The repository does not store the screenshot, account identifiers, or conversation content.

| Test | Result | Evidence and boundary |
| --- | --- | --- |
| GEM-A01 Gemini signed-out page load | PASS | `https://gemini.google.com` completed successfully in WebView2. The run used `Persistent`; the explicit `Fresh disposable` variant remains not run. |
| GEM-A02 Start Google sign-in | PASS | User started the expected sign-in flow manually. |
| GEM-A03 Complete embedded sign-in | PASS | User reported successful login without a prohibited workaround. |
| GEM-A04 Return to Gemini | PASS | User returned to Gemini and could access the signed-in experience. |
| GEM-A05 Recreate WebView with same profile | PASS WITH EVIDENCE NOTE | The user followed the `Restart WebView` instruction and reported that login and history remained. Their wording called it a refresh, so the exact clicked control was not independently observed. |
| GEM-A06 Restart full application | PASS | The window was closed, a new process was launched, and the user reported that login state remained without signing in again. |
| GEM-B01 New conversation and visible response | PASS | User reported normal submission and response; no response content was collected. |
| GEM-B02 Follow-up context | PASS | User reported the follow-up response was normal. |
| GEM-B03 Account history | PASS | History loaded after login and again after session restoration. This is Gemini account history, not AI Dock local caching. |
| GEM-B04 Long conversation | NOT RUN | The two-turn check is not a long-conversation or renderer-stress test. |
| GEM-B05 Clipboard | PASS | User reported copy/paste worked normally. |
| GEM-B06 File upload via picker | PASS | User reported attachment selection and upload worked. |
| File drag-and-drop upload | LIMITED | Dragging a file onto the WebView did not work. The supported MVP path is the page's attachment picker unless a later focused drag/drop implementation is approved. |
| GEM-B07 File download | NOT RUN | No download flow was tested. |
| GEM-B08 Microphone permission prompt | PASS | Sanitized event: `permission-request kind=Microphone origin=https://gemini.google.com`. A visible prompt appeared and the user chose Deny. Post-denial media behavior was not separately exercised. |
| GEM-B09 Generic external link behavior | NOT RUN | Authentication and subscription popup behavior were observed, but a generic unrelated external link was not tested. |
| GEM-B10 Reload recovery | PASS | User reported normal response, retained login, and retained conversation after Reload. |
| GEM-B11 Forced renderer/process failure | NOT RUN | WebView recreation was tested; an actual or simulated renderer/process failure was not. |
| GEM-B12 Known purchase boundary | PASS | Sanitized event: `purchase-popup-blocked https://one.google.com`. The subscription popup did not open and no purchase page was entered. |

### Performance observations

The signed-out page screenshot showed:

- 6 WebView processes;
- approximately 1.2% sampled CPU at that moment;
- 702.8 MB from the original aggregate working-set implementation.

The 702.8 MB value must not be used as a product result because shared memory may be counted in more than one process. The implementation now reports aggregate private memory, but that corrected metric has not yet been measured in a fresh run. Cold start time, time to usable page, streaming CPU, ten-turn behavior, inactive behavior, and post-recovery behavior remain unmeasured.

### Compatibility conclusion

Gemini is **technically viable and remains Experimental** on the tested environment.

The previously identified fatal risk—Google rejecting embedded authentication—did not occur in this run. Authentication, session persistence, ordinary text chat, account history, clipboard, picker-based file upload, explicit microphone permission, reload recovery, and the tested subscription boundary all worked.

This does not establish official Google approval or universal compatibility. Provider behavior can vary by account type, region, policy, and future web changes. Public compatibility language must remain environment-scoped until the remaining gates and repeat runs pass.

### Known limitations and open work

- Run the explicit `Fresh disposable` profile case.
- Re-measure private memory and CPU after the metric correction.
- Perform a longer conversation/stress run and observe whether answers ever require reload.
- Test file download behavior.
- Test generic external-link routing.
- Test a real or controlled renderer/process failure and recovery path.
- Test inactive/suspend behavior and resource reclamation.
- Repeat authentication with any supported account classes that materially differ, without recording identifiers.
- Repeat on at least one other supported Windows/WebView2 environment before a public support claim.
- Add regression coverage for URL sanitization and known purchase-route matching.
- Treat file drag-and-drop as a documented limitation unless a focused implementation is later approved.
- Revalidate purchase routes when Gemini or Google One navigation changes.

### Decisions deliberately deferred

- Whether Gemini should ship as `Verified`, `Limited`, or `ExternalOnly` in the first public release.
- Whether a provider-supported system-browser OAuth handoff exists and is needed as a fallback.
- General external-navigation allowlist policy beyond the tested authentication and subscription cases.
- Formal installer/runtime packaging and Store distribution details.
- Multi-provider WebView lifecycle and memory budget thresholds.
- Whether native drag-and-drop bridging is worth the added complexity for MVP.
