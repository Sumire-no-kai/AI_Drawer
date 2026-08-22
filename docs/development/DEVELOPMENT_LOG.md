# AI Dock Development Log

This is the durable development record for implementation decisions, verified behavior, limitations, and open work. It is not a release changelog. Planned behavior must not be presented as shipped or provider-compatible behavior.

## 2026-08-22 — Isolated x64 M2.2 runtime validation

### Verified behavior

- Added a Debug-only `AI_DRAWER_TEST_DATA_ROOT` override so direct-run tests use a generated temporary AI Drawer data root instead of `%LocalAppData%`. Release builds ignore the variable. The session store and WebView2 profile root share that one test root, preventing a test from changing a regular user's native session or provider profile.
- Added a framework-free application recovery harness. Seven checks passed: corrupt session write blocking and explicit backup; oversized session rejection; newer-schema rejection; invalid DPAPI locator metadata preservation; exclusive-file lock handling; 101-item persistence truncation to the configured 100-workspace limit; and the actual native recovery UI's **Back up and continue** path.
- Direct unpackaged x64 UI automation verified first-run Welcome `Continue`, Settings, the BMC support entry, reopened Welcome `Skip`, and native workspace creation/close. The separate webview-init run created an isolated ChatGPT workspace and its dedicated WebView2 profile without login or provider interaction.
- In that generated no-account profile, four matched WebView2 child processes were deliberately terminated. The native application window and ChatGPT workspace identity survived, then a same-profile WebView2 child process was recreated. No unrelated browser or WebView2 process was selected.

### Verification boundary

- All application runs were direct from the repository's unpackaged Debug output. No MSIX/package registration, installation, Start-menu entry, provider login, prompt, response, cookie, credential, or payment interaction occurred. Temporary test roots were removed after each completed run.
- This is not a provider compatibility result. Real encrypted-locator restart; renderer-unresponsive, OOM, GPU/utility, and multi-workspace fault categories; controlled authentication popup return; external/purchase popup policy; provider-profile reset; fresh-profile deletion after a real WebView2 child exit; measurable live-view pressure; accessibility breadth; x86; ARM64 device; and Windows 10/11 matrix validation remain open.

## 2026-08-22 — Windows on ARM64 build and publish path

### Implemented behavior

- Added the production application's `win-arm64` file-system publish profile and wired the application project to select architecture-specific publish profiles, matching the existing Compatibility Lab pattern.
- The profile is self-contained and preserves the existing non-single-file setting. It creates a repository-local, unsigned development publish directory; it does not register or install the application and does not represent a signed public MSIX release.
- Documented the required lowercase `win-arm64` runtime identifier. The current .NET SDK translates `--arch ARM64` to invalid `win-ARM64`, so ARM64 build guidance uses `--runtime win-arm64 -p:Platform=ARM64`.

### Verification boundary

- Restored the `win-arm64` runtime assets and built the unpackaged x64-hosted ARM64 Debug target with zero warnings and zero errors.
- Restored the Release ReadyToRun asset set and produced a self-contained ARM64 Release file-system publish directory under `src\AIDrawer.App\bin\Release\net10.0-windows10.0.26100.0\win-arm64\publish`.
- No ARM64 executable was launched, no package was registered or installed, and no Windows on ARM device was available. ARM64 runtime behavior, WebView2 availability, tray/hotkey behavior, and MSIX signing/install validation remain open.

## 2026-08-22 — Recoverable session, popup, failure, and lab cleanup hardening

### Implemented behavior

- Replaced the session store's ambiguous empty-load fallback with explicit outcomes for missing, corrupt, oversized, unsupported/newer-schema, temporarily unavailable, and encrypted-locator recovery cases. Any non-safe outcome now blocks session writes.
- Added a startup recovery surface. It allows a safe retry, explicit exit, or an explicit **Back up and continue** path. The backup moves the original `workspaces-v1.json` to a timestamped local recovery file before the write gate opens; failed backup keeps the gate closed. This preserves the existing file instead of silently replacing it with an empty or lossy session.
- Added process-failure differentiation: the first renderer-unresponsive event waits without destroying the page; a renderer exit gets one bounded reload then same-profile recreation; a browser-process exit resets the shared environment and recreates the active workspace while leaving inactive workspaces recoverable; out-of-memory releases inactive live views without an automatic reload loop; frame/GPU/utility failures offer non-destructive recovery guidance.
- Replaced the previous `NewWindowRequested` behavior that navigated the active WebView in place. Allowed provider/authentication origins now open a single controlled, non-persisted native popup using the same provider profile; purchase popups are blocked and unrelated safe HTTPS popups use the system browser without query/fragment values. This is implementation, not provider-specific OAuth verification.
- Compatibility Lab `Fresh disposable` profiles are now deleted after the WebView is closed and its processes have a short release window. Cleanup retries only a generated `fresh-*` path underneath the current test root, refuses paths outside that root or through reparse points, and records a deferred cleanup rather than deleting an unsafe target.
- Updated `CONTEXT.md` with the recovery-blocked session and controlled provider-popup terms and their invariants. The `MainPage` dual view/selection state was deliberately not mechanically split: no small extraction removes its real UI coordination role, and a `WorkspaceCollection` refactor remains a post-runtime-gate decision rather than speculative churn.

### Verification boundary

- Static diff/format checks were run. The repository-local SDK at `D:\DevTools\dotnet\dotnet.exe` built the unpackaged x64 Debug production project with zero warnings and zero errors, and the framework-free Core harness completed all nine checks. The unpackaged x64 Debug Compatibility Lab also compiled; NuGet emitted `NU1900` because this restricted environment could not reach its vulnerability-data index, so it is not a clean dependency-audit result. No SDK, package, app, Start-menu registration, or provider profile was installed or changed.
- Runtime verification remains required for every new recovery path: corrupt/locked/oversized/newer-schema session files; DPAPI locator failure; renderer/browser/OOM event handling; provider sign-in popup return; external/purchase popup policy; and fresh-profile deletion after real WebView2 process exit. No provider compatibility status changed.

## 2026-08-22 — Full code review and M2.2 correctness hardening

### Review scope and changes

- Reviewed every maintained C# and XAML source file in the production application, pure Core project, framework-free policy checks, and Compatibility Lab. The review covered bounds, null and async behavior, Win32/WebView2 calls, disposal, persistence ordering, privacy boundaries, lifecycle behavior, module depth, deletion candidates, and PRD/ADR conformance.
- Corrected the `CoWaitForMultipleObjects` P/Invoke handle-count width from 64-bit to the Win32 `ULONG` width, constrained application-owned native imports to the Windows system directory, and configured both production and lab WebViews to disable host-object access and web messaging in addition to the existing DevTools/autofill/password restrictions.
- Made the shared WebView2 environment lazy and retryable after creation failure. Provider/Home transitions now share the coordinator serialization seam, WebView initialization uses a generation check so shutdown cannot reactivate a disposed view, failed initialization no longer reports an active live workspace, and inactive workspace error state is replayed when selected.
- Added operation-aware disposal protection for known navigation, permission, and download work. The lifecycle policy does not auto-release those views even at the hard cap; when no safe victim exists, activation is blocked with a retry message. The active view and native workspace identity remain protected as before.
- Separated the transient same-process navigation target from the restricted encrypted restart locator. Successful navigation and same-document source changes are observed without reading DOM, query parameters and fragments remain excluded, leaving a reviewed path clears stale persisted identity, and enabling exact restore uses the latest provider-policy-captured locator.
- Corrected remembered-permission copy and behavior to name the sanitized requesting origin and provider-profile scope. Background-workspace requests fail closed instead of placing a prompt over a different active workspace.
- Made provider-profile reset return an explicit success result, keep its temporary profile view alive through `ClearBrowsingDataAsync`, preserve the error instead of immediately hiding it with a reload, disclose full provider-profile browsing-data/sign-out scope, and clear locator metadata for same-provider native tabs that were never instantiated during the current process.
- Enforced one 100-workspace bound across native creation, save, and load; revalidated restore locators at the durability boundary; stopped DPAPI protection failure from overwriting a previously good session with a lossy document; queued complete immutable session/settings snapshots through one ordered writer; and added a frozen final snapshot before explicit Exit. Restored provider tabs now start in a visible reload-required lifecycle state.
- Closed async transition races found in the post-fix review: only the latest workspace selection may update native UI, activation/restart failure keeps actions disabled, exact restore refreshes from the last committed locator on every successful selection, and Exit freezes events and disposes WebViews before its final ordered writes. Provider-profile reset now durably clears every same-provider native locator before clearing browsing data and cannot overlap another selection or reset.
- Removed shallow or dead code that passed the deletion test: the one-use `AppLinks` wrapper, unused `WorkspaceSession.Empty`, and an always-true `isActive` parameter. Retained the pure Core policies, coordinator, per-provider WebView module, sanitized lab event helper, persistence DTOs, and separate lab/product provider catalogs because deleting or merging them would move complexity across the seam or mix test-candidate and product-support semantics.
- Made Debug lab builds explicitly unpackaged with WinApp run registration disabled, disabled its host-object/web-message bridge, restored its empty-state element after a test ends, and removed unused WinUI template comments.

### Verification boundary

- `D:\DevTools\dotnet\dotnet.exe build src\AIDrawer.App\AIDrawer.App.csproj --configuration Debug --arch x64 --no-restore -p:TreatWarningsAsErrors=true` completed with zero warnings and zero errors.
- The production and Core-check projects also completed a `Recommended` .NET analyzer build with warnings treated as errors; repository formatting verification completed without changes.
- The framework-free Core harness completed all nine locator/lifecycle checks, including operation-protected hard-cap selection and negative-grace rejection.
- The unpackaged x64 Debug Compatibility Lab build completed with no compilation error. NuGet emitted `NU1900` because the restricted environment could not reach the remote vulnerability index; this was not treated as a clean dependency-audit result.
- An x86 no-restore build was attempted but stopped before compilation because the existing assets file contains only the restored x64 target. No additional runtime pack was restored or installed. x86 and ARM64 therefore remain unverified.
- No application was launched, installed, registered, or added to the Start menu during this review. The new WebView lifecycle, profile reset, source-change restoration, permission, and shutdown behavior still require focused unpackaged runtime verification with disposable unsigned-in profiles.

### Remaining architectural decisions and risks

- Session load still maps missing, corrupt, oversized, unsupported-schema, and temporary I/O failures to the same empty result. Fixing this without silently overwriting or unexpectedly retaining a damaged file requires an explicit product choice: preserve and block writes pending user action, quarantine a recoverable backup and start fresh, or another disclosed policy.
- Renderer-unresponsive, renderer-exit, browser-exit, utility/GPU failure, and OOM states still share one recovery message. The required failure-specific wait/reload/recreate/environment-reset ladder remains M2 Gate work and needs controlled fault verification.
- Allowed provider/auth popups still navigate the current WebView. Provider-specific OAuth/opener and popup policy remains unverified and may require a controlled native popup or external-browser adapter.
- The Compatibility Lab's timestamped `Fresh disposable` data directory is isolated but is not yet automatically deleted after WebView2 releases its processes. Cleanup must validate the resolved generated path before any recursive deletion.
- `MainPage` remains an oversized composition module with native active state duplicated by `WorkspaceCoordinator`. A future deep `WorkspaceCollection` aggregate may absorb identity, ordering, active transition, naming, restore, and immutable snapshot invariants, but a mechanical partial-class split or a DI/repository layer was rejected as shallow indirection.

#### OPEN decision: preserve failed session loads without silent overwrite

This section records an assessed problem and candidate resolution only. No recovery behavior described below has been implemented or verified.

**Current verified behavior**

- `WorkspaceSessionStore.LoadSessionAsync` currently returns the same empty result when no session exists, the file exceeds the size limit, JSON or schema validation fails, or file access fails temporarily.
- `UnprotectAsync` separately maps every protected-locator decryption failure to `null`. The remaining workspace metadata still loads, but a later save can replace a potentially recoverable encrypted locator with no locator because invalid ciphertext and a temporary DPAPI failure are not distinguished.
- A genuinely missing file is a normal first-run condition. The other outcomes are not equivalent: after the empty result is accepted, an ordinary selection or Exit save can replace the previous session file with a valid but empty layout.
- The browser profile and provider-managed conversation history are not deleted by that overwrite, but native workspace identity, order, names, Keep active state, provider assignment, and usable restore locators can be lost. A newer application version's session can also be unintentionally downgraded by an older build.

**Candidate approaches**

1. **Guarded recovery state — recommended.** Return a typed load result such as `Missing`, `Loaded`, `LoadedWithLocatorFailures`, `Corrupt`, `Oversized`, `UnsupportedSchema`, or `TemporarilyUnavailable`. Allow normal writes only for `Missing` and `Loaded`. Every other result preserves the original file and blocks automatic session writes while a native recovery surface offers `Retry` and an outcome-specific backed-up continuation.
2. **Partial salvage.** Load valid workspace records while retaining unreadable records and encrypted locator blobs for a later retry. This improves availability but introduces merge, ordering, active-workspace, migration, and repeated-save semantics that are disproportionate for the current schema. It should be considered only if real failure evidence shows that whole-session recovery is too disruptive.
3. **Automatic quarantine and blank start.** Rename the failed file and immediately continue with an empty layout. This is simpler but makes a material recovery choice without consent and can conceal a temporary lock or downgrade attempt. It is not recommended as the default.
4. **Keep the current silent fallback.** This has the smallest code change but permits silent native-workspace loss and is rejected for a browser-like recovery product.

**Recommended behavior for evaluation**

- Treat only an actually missing session file as first run. Malformed JSON, size rejection, unsupported schema, DPAPI/unprotect failure, access denial, sharing violation, and other I/O failures must remain distinguishable.
- On a protected failure, keep the original file untouched and enter a write-blocked recovery state before any workspace-selection or shutdown save can run. Settings persistence remains independent and does not need to be blocked.
- `Retry` performs another read without modifying either the primary file or any temporary file.
- For whole-document failures, `Back up and start blank` requires explicit confirmation. For `LoadedWithLocatorFailures`, the narrower action is `Back up and continue without unavailable exact locations`, retaining all valid native workspace metadata. Both actions atomically rename the original to a timestamped backup inside the AI Drawer application-data directory, validate that the source and destination resolve inside that directory, and enable a replacement write only after the rename succeeds. If backup creation fails, the write block remains in place.
- The recovery surface must explain that provider profiles and provider-hosted conversations are not being deleted, while native workspace metadata may be unavailable until recovery succeeds.
- The design adds no cloud service, telemetry, account system, DOM access, or storage of prompts and responses.

**Proposed implementation and verification sequence — not approved yet**

1. Introduce the typed load outcome and preserve the underlying failure category without exposing sensitive file contents in UI or diagnostics.
2. Add one session-write gate owned by the persistence boundary; UI call sites must not individually decide whether overwriting is safe.
3. Add the recovery surface and explicit backup/start-over or backup-and-sanitize transaction, then connect normal startup only after the outcome is resolved.
4. Add deterministic checks for missing, valid, malformed, oversized, newer-schema, undecryptable-locator, locked/access-denied, backup-success, backup-failure, retry-success, and shutdown-during-recovery cases.
5. Run repository-local unpackaged restart verification and confirm that every failure case preserves the original bytes until the user explicitly approves backup and reset.

Before implementation, product review must confirm whether the recommended write-blocking startup is acceptable, whether partial read-only workspace display is worth its extra complexity, and how many local backup generations should be retained.

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
