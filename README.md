# AI Drawer

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

Development is in **Milestone 0: Provider Compatibility Lab**.

| Provider | Current status | Evidence boundary |
| --- | --- | --- |
| Gemini | `Experimental` | Embedded sign-in, session persistence, ordinary text chat, account history, clipboard, picker-based upload, explicit microphone permission, reload, and one known subscription-popup boundary worked in one Windows test environment. Long-conversation, download, generic external-link, renderer-failure, corrected resource, and repeat-environment tests remain open. |
| Other providers | `Not tested` | Candidate providers must pass the PRD compatibility gates before they can be advertised as supported. |

The Gemini result shows technical feasibility only. It is not a statement of official Google approval or universal compatibility across accounts, regions, policies, or future website versions.

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
- safe system-browser handoff for unrelated navigation;
- clear permission and compatibility states;
- wait, reload, and same-profile restart recovery;
- blocking and explanation of known in-app provider purchase flows;
- keyboard navigation, high contrast, reduced motion, and readable light/dark themes;
- MSIX packaging, with Microsoft Store as the intended stable distribution channel.

macOS may be explored later as a separate native shell using SwiftUI/AppKit and WKWebView. It is not part of the Windows MVP.

## Technology

| Area | Choice |
| --- | --- |
| Language and runtime | C# and .NET |
| Native UI | WinUI 3 and Windows App SDK |
| Embedded web runtime | Microsoft Edge WebView2 Evergreen Runtime |
| Windows integration | Win32 interop where required |
| Primary packaging | MSIX |

## Project documents

- [Product requirements](AI_Dock_PRD_v0.2.md)
- [Product principles](PRODUCT.md)
- [Design system](DESIGN.md)
- [Repository working agreement](AGENTS.md)
- [Apache License 2.0](LICENSE)

Build and contributor instructions will be added as the compatibility harness is prepared for review on the main branch. Provider compatibility contributions must include reproducible environment details and test evidence; adding a URL alone does not establish support.

## Roadmap

1. Complete the initial provider compatibility matrix and resource baselines.
2. Build the minimal native Windows shell and lifecycle behavior.
3. Add bounded multi-provider workspaces and recovery.
4. Harden navigation, privacy, purchase, and security boundaries.
5. Package a public beta and publish provider limitations.

## Independence

AI Drawer is an independent, unofficial open-source project. It is not affiliated with, endorsed by, or sponsored by the AI service providers whose official websites may be opened inside the application. Provider subscriptions are purchased from and managed by their respective providers.

## License

Licensed under the [Apache License 2.0](LICENSE).
