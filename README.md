# AI Drawer

![Repository version](https://img.shields.io/badge/version-0.0.1--dev-181717?style=flat-square&labelColor=181717)
![Development status](https://img.shields.io/badge/status-M4%20MVP%20Completion%20Under%20Review-181717?style=flat-square&labelColor=181717)
![Initial platform](https://img.shields.io/badge/platform-Windows%2010%201809%2B%20%7C%2011-181717?style=flat-square&labelColor=181717)

> A lightweight, privacy-respecting Windows workspace for the AI web apps you already use.
>
> 一个轻量、隐私克制的多 AI Windows 桌面快捷入口。

AI Drawer is an open-source Windows desktop shell for opening, resuming, and switching between official browser-based AI services. It is designed for people who use multiple providers or keep several independent work contexts open within the same provider.

> [!IMPORTANT]
> AI Drawer is currently **pre-alpha**. There is no public end-user build yet. A provider appearing in the roadmap is a test candidate, not a compatibility claim.

## Why AI Drawer?

- Open the right AI workspace from one keyboard-first desktop entry point.
- Keep AI sessions separate from everyday browser tabs and extensions.
- Switch between different providers or multiple workspaces from the same provider.
- Preserve provider-managed sign-in and history without creating a second conversation store.
- Bound the number of live WebViews instead of leaving every visited service running.
- Use existing provider subscriptions directly—no API keys, AI Drawer account, or additional AI subscription.

## Product direction

The Windows MVP is a native **C# / .NET / WinUI 3** application that hosts compatible official websites with **Microsoft Edge WebView2**. The native shell manages window behavior, workspaces, safe navigation, permissions, recovery, and WebView lifecycle. Each provider continues to own its authentication, conversations, history, model selection, uploads, and account settings.

The provisionally approved interface is deliberately small: one compact workspace bar above the active provider page. It does not include a browser address bar, persistent sidebar, provider-card dashboard, or custom conversation UI.

## Current status

The shell now contains the **Milestone 2.2 workspace persistence and lifecycle implementation**, on top of the M2 multi-provider foundation. Native workspace identity, order, provider assignment, Keep active preference, and the selected workspace are persisted independently of live WebViews. The provisional Balanced policy keeps a bounded third live view during a five-minute grace period, then converges to two after the grace period expires and all protected operations finish. A fourth opening releases an eligible inactive WebView without deleting its native workspace; if every possible victim is completing a known navigation, permission request, or download, the new activation waits without breaking that protected operation or exceeding the hard live-view cap, then retries automatically when one finishes.

M3 implementation now applies a fail-closed, exact-origin navigation boundary while currently unavailable M2 tests remain explicitly deferred rather than passed. Main workspaces, their frames, and controlled provider popups embed only reviewed HTTPS provider or authentication origins; certificate-error navigation is explicitly cancelled. Unreviewed top-level external links require native confirmation before a query- and fragment-stripped handoff to the system browser, while external frames remain blocked. Known purchase paths are cancelled and show a native explanation only: AI Drawer never opens or processes provider purchases. This does not change any provider compatibility status or establish that provider-specific authentication, external-link, billing, checkout, or payment flows have passed runtime validation.

M4 quality work now includes a pinned .NET SDK, clean-checkout CI with x64/x86/ARM64 production builds, privacy-safe issue templates, GitHub Private Vulnerability Reporting, release-gate documentation, and a manually triggered internal MSIX candidate workflow. The workflow can generate separately validated x64, x86, or ARM64 candidates in explicit framework-dependent or self-contained modes. Each candidate is deliberately unsigned, carries SHA-256 checksums for every emitted MSIX and machine-readable source/package/dependency metadata, and is explicitly marked as ineligible for public release. Its validation checks package architecture, disabled startup-task declaration, the reviewed `runFullTrust` capability set, signature/private-key absence, runtime dependency evidence, and complete checksum metadata. It validates the packaging pipeline only: it is not a Public Beta and does not resolve signing, public identity/versioning, the final dependency-mode decision, website deployment, or deferred provider/device Gates.

The native recovery ladder now includes an explicit system-browser fallback that uses only a reviewed provider HTTPS origin and path after removing query parameters and fragments. A top-level Feedback & Support surface keeps public bug/provider evidence, private vulnerability reporting, and optional development support visibly separate; opening this native surface creates no provider WebView and never requests provider account content.

The native MVP settings and shell controls are now implemented: a default provider, configurable or disabled global shortcut, packaged launch-on-startup registration, close-to-tray versus true-exit behavior, always-on-top, clamped window placement restore, and a tray action for the default provider. Launch-on-startup creates the window shell hidden and does not initialize provider WebViews. Provider-local disk-cache clearing, one-provider website-data reset, and stronger two-step reset-all are separate actions with explicit sign-out scope and partial-failure reporting. Downloads receive a sanitized, non-overwriting local path; every download displays its destination before confirmation, with stronger language for executable and uncommon file types, and AI Drawer never opens the result automatically.

For reviewed provider URL shapes, the implementation can store one current-user-encrypted restore locator containing only the exact provider HTTPS origin and an allowlisted opaque conversation path. Query parameters, fragments, authentication hosts, subdomains, custom ports, and unknown paths are rejected, and the value is revalidated before storage and use. A separate non-persisted, app-domain-only URL can preserve a safer same-process return target after WebView disposal without weakening the restart privacy boundary. ChatGPT, Claude, and Gemini currently have provisional persisted-path rules; the other provider entries intentionally fall back to provider home after restart until a safe rule is reviewed. The three-screen, versioned first-run privacy disclosure, About & Privacy surface, disabled-until-measured memory-mode selector, exact-restore control, Keep active control, local support-reminder policy, and expanded tray/settings surfaces are also present.

The optional support entry opens Edward Lee's shared [Buy Me a Coffee page](https://buymeacoffee.com/edward_lee) for independent projects. Contributions do not activate or unlock AI Drawer, provider services, subscriptions, accounts, premium features, or support plans.

The latest merged-host evidence passes production x64, x86, and ARM64 Debug/Release builds with warnings treated as errors, 47 Core checks, 21 non-GUI application checks, and 25 checks with isolated UI Automation. The current release-hardening branch adds a release-contract check and passes 22 non-GUI checks plus 26 complete checks in three consecutive final UI stability runs. The no-account runtime harness also passes a real five-minute lifecycle/recovery run on the recorded Windows 11 x64 host. The UI suite covers Welcome/restart recovery, corrupt-session backup, Settings, the seven-open support reminder and both dismissal choices, Feedback & Support, minimum-window layout, close-to-exit behavior, provider policy, and fixed external-link routing from Home and Settings. A dedicated live run asked Windows to open the fixed BMC and Microsoft Forms URLs and recorded only Windows' success result; it did not inspect browser tabs, accounts, or content. These results do not register or sign a final package, use provider accounts, submit prompts, visit payment flows, or establish Windows 10/ARM64-device/provider compatibility. There is no public end-user build yet.

| Provider | Current status | Evidence boundary |
| --- | --- | --- |
| Gemini | `Experimental` | Embedded sign-in, session persistence, ordinary text chat, account history, clipboard, picker-based upload, explicit microphone permission, reload, and one known subscription-popup boundary worked in one Windows test environment. Long-conversation, download, generic external-link, renderer-failure, corrected resource, and repeat-environment tests remain open. |
| Grok | `Limited` | X sign-in and basic conversation submission worked, and same-profile WebView recreation retained login in one manual run. Reply text rendered blank inside the tested environment even though user-initiated copy returned the reply. A normal-browser comparison is still required before assigning the cause. The provider purchase page also remained reachable. |
| ChatGPT, Claude, DeepSeek, Doubao, Qwen Studio, GLM | `Experimental` | Initial page, sign-in, and basic-use checks were reported usable in one manual sweep. Required feature, recovery, resource, repeat-environment, and purchase-boundary coverage remains incomplete; ChatGPT's purchase page remained reachable. |
| Microsoft Copilot (Personal) | `Experimental` | The consumer entry and exact reviewed application origins are configured. Login origins, session restoration, page behavior, popups, and purchase routes have not been manually validated, so no provider compatibility claim is made. |
| Tongyi Qianwen (China), Z.ai (International) | `Not tested` | Regional websites are separate candidates with isolated profiles. Compatibility evidence from their related provider entry does not transfer. |

These results show environment-scoped technical feasibility only. They are not statements of provider approval or universal compatibility across accounts, regions, policies, or future website versions.

See the [manual M0 test plan](docs/testing/Provider_WebView2_Compatibility_Test_Plan.md) and [compatibility matrix](docs/testing/Provider_Compatibility_Matrix.md) for the current evidence boundary.

## Privacy boundary

AI Drawer is intentionally not AI middleware. Native application code must not read, scrape, synchronize, or store:

- prompts or responses;
- conversation content or page DOM;
- credentials, cookies, or authentication tokens;
- request bodies or payment information.

WebView2 still maintains ordinary browser-origin data such as cookies and local storage inside AI Drawer's application-specific profiles so that provider sessions can persist. AI Drawer does not import or reuse the user's normal Edge or Chrome profile.

The Compatibility Lab can show up to 100 debug-only, in-memory sanitized event codes while investigating provider compatibility. It never stores URLs, page content, credentials, cookies, tokens, or payment information, writes no diagnostic file, and has no network diagnostics or analytics output. Release builds intentionally show no diagnostic events.

## Planned Windows MVP

- global show/hide shortcut and single-instance behavior;
- system tray integration;
- multiple provider and same-provider workspaces;
- lazy WebView creation with a bounded live-view budget;
- persistent, provider-isolated WebView profiles;
- first-run welcome, feature, privacy, compatibility, tray, and data-reset explanations;
- settings for shortcut, startup, tray, window, memory mode, workspace restoration, and provider-profile data controls;
- provider cache clearing, provider website-data reset, and reset-all controls with accurate sign-out scope;
- a disclosed Buy Me a Coffee link under About & Support, plus an infrequent local-only reminder that can be permanently dismissed;
- user-confirmed, sanitized system-browser handoff for unrelated navigation;
- clear permission and compatibility states;
- wait, reload, and same-profile restart recovery;
- blocking and explanation of known in-app provider purchase flows;
- keyboard navigation, high contrast, reduced motion, and readable light/dark themes;
- MSIX packaging, with Microsoft Store as the intended stable distribution channel.

macOS may be explored later as a separate native shell using SwiftUI/AppKit and WKWebView. It is not part of the Windows MVP.

## Provisional workspace lifecycle

The MVP treats one provider entry as one signed-in WebView2 profile. Multiple conversation workspaces for the same provider share that profile's sign-in, cookies, cache, permissions, and provider-managed history. Simultaneous isolated accounts for the same provider are deferred until after the MVP.

The implemented provisional lifecycle combines three states rather than keeping every provider page live:

```text
Active normal
    → recently used low-memory
    → disposed but natively identifiable and reloadable
```

- The active workspace is never automatically released.
- A workspace in a native-known navigation, permission, or download operation is not selected for automatic release; a new activation is blocked with a retry message if the hard cap has no safe victim.
- Recently active and user-protected workspaces receive a grace period.
- Low-memory views may continue provider scripts and network activity. Suspension remains a future measured option and is not implemented by the current Balanced policy.
- Disposed workspaces retain only minimal native metadata and, where a provider-specific policy makes it safe, one restricted local restore locator. AI Drawer does not cache prompts, responses, DOM, or conversation content.
- If a persisted session cannot be safely read, AI Drawer blocks session writes and offers Retry, Exit, or **Back up and continue**. The latter moves the original local session to a timestamped recovery backup before a new session can be written; it does not silently replace it with an empty layout.
- A provider request to open an allowed app or authentication popup receives one controlled, non-persisted native window sharing the same provider profile. Known purchase popups remain blocked; unrelated safe HTTPS links require native confirmation before a query- and fragment-stripped system-browser handoff.
- `Low Memory`, `Balanced`, and `Fast Switching` modes will use measured live, suspended, and restoration budgets rather than one unqualified global number.
- A released workspace must remain at least as identifiable and recoverable as an inactive Chrome tab; resource savings do not justify silently replacing it with an unrelated new context.

Initial planning assumes an 8 GB Windows device should handle one active view, one warm or suspended view, and at least ten recoverable workspaces. A 16 GB device is the recommended target for two or three live views plus additional suspended and disposed workspaces. These are design targets only: the repository does not yet contain the cross-provider, Windows 10/11, long-conversation, or memory-pressure evidence needed to publish a capacity claim.

## Technology

| Area | Choice |
| --- | --- |
| Language and runtime | C# and .NET |
| Native UI | WinUI 3 and Windows App SDK |
| Embedded web runtime | Microsoft Edge WebView2 Evergreen Runtime |
| Windows integration | Win32 interop where required |
| Primary packaging | MSIX |

## Development dependencies

The current Windows shell is developed and built with:

| Dependency | Purpose | License |
| --- | --- | --- |
| .NET 10 SDK | C# build tooling | MIT |
| Windows App SDK 2.2.0 | WinUI 3 application platform | MIT |
| Microsoft Edge WebView2 Evergreen Runtime | Provider-owned web content runtime | Microsoft Software License Terms |
| H.NotifyIcon.WinUI 2.4.1 | Windows notification-area icon integration | MIT |
| Microsoft Windows SDK Build Tools | MSIX build and package tooling | MIT |

Third-party notices are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Dependencies are used only for the native desktop shell; AI Drawer does not bundle, proxy, or modify provider web applications.

## Build the current development shell

This is contributor-only guidance, not an end-user installation path. On Windows 10 version 1809 or later, or Windows 11, with the .NET 10 SDK and the WebView2 Evergreen Runtime available:

```powershell
dotnet restore src/AIDrawer.App/AIDrawer.App.csproj
dotnet build src/AIDrawer.App/AIDrawer.App.csproj --configuration Debug --arch x64
```

For Windows on ARM, use the lowercase runtime identifier explicitly (the CLI `--arch ARM64` form produces an invalid `win-ARM64` identifier with the current SDK):

```powershell
dotnet restore src/AIDrawer.App/AIDrawer.App.csproj --runtime win-arm64
dotnet build src/AIDrawer.App/AIDrawer.App.csproj --configuration Debug --runtime win-arm64 -p:Platform=ARM64
dotnet publish src/AIDrawer.App/AIDrawer.App.csproj --configuration Release --runtime win-arm64 -p:Platform=ARM64 -p:PublishProfile=win-ARM64
```

The ARM64 profile produces a self-contained file-system publish directory under `bin\Release\...\win-arm64\publish`. It is an unsigned development artifact, not an installable public MSIX release; MSIX signing, provenance, and ARM64-device validation remain release gates.

Debug builds default to the repository-local unpackaged profile. In Visual Studio, select **AI Drawer (Unpackaged)**; this runs the project without installing or registering AI Drawer and does not create a Start-menu entry. The separate **AI Drawer (Package — registers app)** profile is only for deliberate MSIX testing and may require Windows Developer Mode. No installer, signing configuration, or public release artifact is included yet.

To inspect the current unsigned packaging pipeline without installing or publishing anything, use a clean working tree and run:

```powershell
./tools/Build-InternalBetaCandidate.ps1 -CandidateLabel local-framework -DependencyMode FrameworkDependent
./tools/Build-InternalBetaCandidate.ps1 -CandidateLabel local-self-contained -DependencyMode SelfContained
./tools/Test-BetaCandidate.ps1 -CandidateDirectory artifacts/beta-candidates/local-self-contained
```

The builder creates an ignored directory under `artifacts/beta-candidates`, verifies the unsigned package contract, and writes complete MSIX checksums, machine-readable dependency/size/source metadata, and the current known limitations. The independent verifier reopens the candidate, checks every recorded hash and byte total, compares package identity/version/publisher/architecture, inspects signature state and private-key extensions, and can compare a post-download file to the expected SHA-256. `-AllowDirtySource` is only for local probes and is recorded in metadata. See the [dependency-mode evidence](docs/release/PACKAGING_MODE_EVIDENCE_2026-08-30.md), [Public Beta release checklist](docs/release/BETA_RELEASE_CHECKLIST.md), [Store-submission preparation](docs/release/STORE_SUBMISSION_PREPARATION.md), [versioning and rollback](docs/release/VERSIONING_AND_ROLLBACK.md), and [release-notes template](docs/release/RELEASE_NOTES_TEMPLATE.md) before treating any artifact as releasable.

## Project documents

- [Product principles](PRODUCT.md)
- [Apache License 2.0](LICENSE)

Provider compatibility contributions must include reproducible environment details and test evidence; adding a URL alone does not establish support.

## Roadmap

1. Complete the deferred M2 restart, profile-reset, recovery, accessibility, live-view pressure, architecture, and Windows-version evidence without treating current implementation as acceptance.
2. Complete the remaining M3 provider-specific navigation, popup, purchase-boundary, download, and focused runtime security evidence; the native policy implementation is present but these Gates are not inferred from compilation.
3. Resolve the M4 public product identity/version scheme, MSIX dependency model, and signing path.
4. Build and deploy the required independent static website from its own repository and release lifecycle.
5. Test the exact signed Beta bytes across the approved matrix, then publish a tagged GitHub prerelease with checksums, limitations, and rollback guidance.

## Independence

AI Drawer is an independent, unofficial open-source project. It is not affiliated with, endorsed by, or sponsored by the AI service providers whose official websites may be opened inside the application. Provider subscriptions are purchased from and managed by their respective providers.

## License

Licensed under the [Apache License 2.0](LICENSE).
