# AI Drawer

![Repository version](https://img.shields.io/badge/version-0.0.1--dev-181717?style=flat-square&labelColor=181717)
![Development status](https://img.shields.io/badge/status-M2.2%20Implementation%20Under%20Verification-181717?style=flat-square&labelColor=181717)
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

The shell now contains the **Milestone 2.2 workspace persistence and lifecycle implementation**, on top of the M2 multi-provider foundation. Native workspace identity, order, provider assignment, Keep active preference, and the selected workspace are persisted independently of live WebViews. The provisional Balanced policy keeps a bounded third live view during a five-minute grace period, then converges to two after the grace period expires and all protected operations finish. A fourth opening releases an eligible inactive WebView without deleting its native workspace; if every possible victim is completing a known navigation, permission request, or download, the new activation is blocked and asks the user to retry instead of breaking that protected operation or exceeding the hard live-view cap.

For reviewed provider URL shapes, the implementation can store one current-user-encrypted restore locator containing only the exact provider HTTPS origin and an allowlisted opaque conversation path. Query parameters, fragments, authentication hosts, subdomains, custom ports, and unknown paths are rejected, and the value is revalidated before storage and use. A separate non-persisted, app-domain-only URL can preserve a safer same-process return target after WebView disposal without weakening the restart privacy boundary. ChatGPT, Claude, and Gemini currently have provisional persisted-path rules; the other provider entries intentionally fall back to provider home after restart until a safe rule is reviewed. The first-run privacy explanation, Settings foundation, disabled-until-measured memory-mode selector, exact-restore control, Keep active control, About & Support surface, local support-reminder policy, and tray Settings entry are also present.

The optional support entry opens Edward Lee's shared [Buy Me a Coffee page](https://buymeacoffee.com/edward_lee) for independent projects. Contributions do not activate or unlock AI Drawer, provider services, subscriptions, accounts, premium features, or support plans.

After the full code-review pass, the production x64 Debug project and Core-check project pass the `Recommended` .NET analyzer set with warnings treated as errors, the production build has zero warnings and zero errors, and all nine framework-free locator/lifecycle policy checks pass. The earlier repository-local unpackaged smoke run remains evidence for Settings and welcome-button pointer activation, but the new lifecycle, reset, navigation, and persistence changes have not received a fresh GUI or provider-account run. This is still not M2 acceptance or a performance claim: corrupt/unsupported session recovery behavior needs a product decision, and encrypted-locator restart restoration, provider-profile reset, renderer-specific recovery, popup behavior, instrumented live-view pressure, provider URL behavior, accessibility breadth, x86/ARM64 compilation, and the required Windows 10/11 matrix remain open. There is no public end-user build yet.

| Provider | Current status | Evidence boundary |
| --- | --- | --- |
| Gemini | `Experimental` | Embedded sign-in, session persistence, ordinary text chat, account history, clipboard, picker-based upload, explicit microphone permission, reload, and one known subscription-popup boundary worked in one Windows test environment. Long-conversation, download, generic external-link, renderer-failure, corrected resource, and repeat-environment tests remain open. |
| Grok | `Limited` | X sign-in and basic conversation submission worked, and same-profile WebView recreation retained login in one manual run. Reply text rendered blank inside the tested environment even though user-initiated copy returned the reply. A normal-browser comparison is still required before assigning the cause. The provider purchase page also remained reachable. |
| ChatGPT, Claude, DeepSeek, Doubao, Qwen Studio, GLM | `Experimental` | Initial page, sign-in, and basic-use checks were reported usable in one manual sweep. Required feature, recovery, resource, repeat-environment, and purchase-boundary coverage remains incomplete; ChatGPT's purchase page remained reachable. |
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
- safe system-browser handoff for unrelated navigation;
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

Debug builds default to the repository-local unpackaged profile. In Visual Studio, select **AI Drawer (Unpackaged)**; this runs the project without installing or registering AI Drawer and does not create a Start-menu entry. The separate **AI Drawer (Package — registers app)** profile is only for deliberate MSIX testing and may require Windows Developer Mode. No installer, signing configuration, or public release artifact is included yet.

## Project documents

- [Product principles](PRODUCT.md)
- [Apache License 2.0](LICENSE)

Provider compatibility contributions must include reproducible environment details and test evidence; adding a URL alone does not establish support.

## Roadmap

1. Decide how corrupt, oversized, temporarily unavailable, or newer-schema session files are preserved and surfaced instead of silently becoming an empty layout.
2. Complete the remaining M2.2 restart, encrypted-locator, provider-profile reset, renderer-specific recovery, popup, broader accessibility, and multi-workspace lifecycle verification.
3. Run the deferred live-view pressure comparison before enabling Low Memory or Fast Switching modes or claiming a capacity.
4. Review safe restore patterns provider by provider; unsupported persisted patterns must continue to fail closed to provider home after restart.
5. Complete the outstanding M0/M2 compatibility and Windows 10/11 evidence, then decide whether the M2 Gate permits entry into M3.

## Independence

AI Drawer is an independent, unofficial open-source project. It is not affiliated with, endorsed by, or sponsored by the AI service providers whose official websites may be opened inside the application. Provider subscriptions are purchased from and managed by their respective providers.

## License

Licensed under the [Apache License 2.0](LICENSE).
