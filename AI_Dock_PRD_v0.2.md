# AI Dock — Product Requirements Document

> Status: Pre-implementation draft
> Version: 0.2
> Last updated: 2026-08-20
> Initial platform: Windows 11
> Future platform: macOS
> Development name: **AI Dock** (temporary; final naming remains open)

---

## 1. Product summary

AI Dock is a lightweight, privacy-respecting desktop switcher for the official web applications of mainstream AI services.

It gives users one keyboard-first desktop entry point for AI services they already use, such as ChatGPT, Claude, Gemini, Grok, DeepSeek, Doubao, Qwen, and GLM, without requiring users to restore their full browser session or manage API keys.

AI Dock is a native Windows desktop shell around third-party web applications. It does not provide AI models, proxy AI traffic through a backend, or replace the providers' official websites.

### Product promise

> One private, keyboard-first desktop home for your existing AI subscriptions.

Chinese positioning:

> 一个轻量、隐私克制的多 AI 桌面快捷入口。

### Primary workflow

```text
User is working normally
        ↓
Press the global shortcut
        ↓
AI Dock appears and receives focus
        ↓
Switch to a preferred AI provider
        ↓
Use the provider's official web application
        ↓
Press the shortcut again or close to tray
        ↓
AI Dock disappears and the user continues working
```

---

## 2. Problem statement

Users increasingly rely on several AI services, often with different strengths and separate subscriptions. Accessing them through a normal browser can be disruptive because it may:

- restore a large browser session with unrelated tabs;
- activate extensions and background processes;
- mix AI sessions with the user's everyday browser profile;
- require switching among several browser tabs or separate official apps;
- provide inconsistent keyboard and window behavior between providers.

Official desktop clients solve this problem for individual providers, and Microsoft Edge can install websites as apps. AI Dock therefore must offer more than a generic website wrapper.

Its differentiated value is the combination of:

- one global shortcut;
- one consistent window and tray experience;
- quick switching across domestic and international AI providers;
- an application-specific browser profile isolated from the user's normal browser profile;
- no API keys and no additional AI subscription;
- no reading, scraping, synchronizing, or storing conversation content;
- an auditable open-source implementation.

---

## 3. Product principles

### 3.1 Provider-owned AI experience

The official provider website remains responsible for:

- authentication;
- conversations and history;
- model selection;
- file upload and download;
- voice and media features;
- subscription status;
- account management;
- provider-specific safety and privacy controls.

### 3.2 Native shell, not AI middleware

AI Dock manages only native desktop behavior, provider selection, safe navigation, WebView lifecycle, and local application settings.

### 3.3 Privacy by architectural restraint

The application must not read or manipulate prompts, responses, conversations, credentials, session cookies, or page DOM.

### 3.4 Compatibility must be earned by testing

A provider must not be advertised as supported merely because its home page loads. It must pass the compatibility criteria in this PRD.

### 3.5 Respect provider security boundaries

The application must not spoof User-Agent values, bypass embedded-browser restrictions, reverse engineer private APIs, or weaken provider authentication controls.

### 3.6 Lightweight is a measured result

Resource efficiency must be compared against realistic baselines. It must not be claimed solely because the application uses WebView2 instead of Electron.

---

## 4. Goals and non-goals

### 4.1 MVP goals

The Windows MVP must:

1. Provide fast global show, hide, restore, and focus behavior.
2. Host compatible official AI web applications using WebView2.
3. Support multiple providers through data-driven provider definitions.
4. Preserve provider sessions in an application-specific WebView2 profile.
5. Limit the number of live WebViews to control CPU and memory use.
6. Keep unrelated external browsing outside AI Dock.
7. Block known subscription and payment flows from completing inside AI Dock.
8. Expose clear compatibility status and limitations for each provider.
9. Store only application settings and WebView-managed browser data.
10. Be distributable as a trustworthy Windows application.
11. Be open source and support optional Buy Me a Coffee donations.

### 4.2 Explicit non-goals

The MVP must not include:

- OpenAI, Anthropic, Google, DeepSeek, or other AI APIs;
- BYOK or API key management;
- a backend server;
- application user accounts;
- AI model routing;
- prompt broadcasting across providers;
- local or bundled model inference;
- agents, tools, terminal execution, or filesystem access;
- RAG, embeddings, or vector databases;
- custom conversation storage or synchronization;
- DOM scraping or content extraction;
- JavaScript injection into provider pages;
- automated clicking or form submission;
- cookie, token, credential, prompt, or response interception;
- browser extensions;
- a general-purpose address bar or unrestricted web browsing;
- an analytics backend;
- cloud synchronization;
- an in-app payment system;
- reselling or receiving commissions from provider subscriptions.

---

## 5. Target users

### Primary users

- People who regularly use two or more AI web applications.
- Users who already pay providers directly and do not want another AI subscription.
- Keyboard-first Windows users who prefer a utility-style workflow.
- Privacy-conscious users who value a clear boundary between native code and conversation content.
- Users who want AI sessions isolated from their normal browser tabs and extensions.

### Not the primary audience

- Users looking for an API client or model router.
- Developers looking for a coding agent.
- Users who need offline AI inference.
- Users expecting AI Dock to unify conversation history across providers.
- Users expecting full browser functionality.

---

## 6. Platform and technical classification

AI Dock for Windows is a **native Windows desktop application with embedded third-party web content**.

### Approved Windows stack

```text
Language                  C#
Runtime and base library  .NET, current supported release selected at implementation time
Native UI                 WinUI 3
Windows platform layer    Windows App SDK
Embedded web runtime      Microsoft Edge WebView2 Evergreen Runtime
Native interoperability   Win32 APIs where Windows App SDK has no direct equivalent
Packaging                 MSIX for primary distribution
```

### Native responsibilities

- application lifecycle and single-instance behavior;
- window creation, placement, focus, restore, and optional always-on-top;
- global shortcut registration;
- system tray integration;
- provider selection and compatibility information;
- WebView creation, suspension, disposal, and crash recovery;
- local settings;
- navigation and purchase boundaries;
- native permission prompts where needed.

### WebView responsibilities

- rendering the provider's official website;
- provider-controlled authentication;
- provider-controlled conversation UI;
- browser-origin storage and cookies;
- provider-supported upload, download, clipboard, audio, and media functionality.

---

## 7. Provider strategy

### 7.1 Initial compatibility candidates

Milestone 0 must test at least:

1. ChatGPT
2. Claude
3. Gemini
4. Grok
5. DeepSeek
6. Doubao / 豆包
7. Qwen / 通义千问
8. GLM / 智谱清言

Additional candidates after the first matrix may include Kimi, Tencent Yuanbao, ERNIE Bot, Microsoft Copilot, Perplexity, Mistral Le Chat, and other mainstream web-based AI services.

This list is a test backlog, not a claim of support.

### 7.2 Compatibility states

Each provider must expose one of these states:

| Status | Meaning |
|---|---|
| `Verified` | Required login, session persistence, basic conversation, navigation, and stability tests passed on the current release. |
| `Limited` | Basic use works, but one or more material features or authentication methods are unavailable. |
| `Experimental` | The provider can be opened, but compatibility is incomplete, unstable, or not fully verified. |
| `ExternalOnly` | The provider cannot be used safely or reliably inside the embedded browser and opens in the system browser instead. |
| `Disabled` | The provider is temporarily unavailable because of a security, policy, or compatibility issue. |

### 7.3 Provider definitions

Provider behavior must be data-driven where practical.

```text
ProviderDefinition
- Id
- DisplayName
- HomeUrl
- AppDomains
- AuthenticationDomains
- PopupDomains
- ExternalLinkPolicy
- PermissionPolicy
- PurchasePolicy
- CompatibilityStatus
- CompatibilityNotes
```

Do not implement provider-specific native UI logic unless a tested compatibility issue requires it.

### 7.4 Custom providers

User-defined and community-contributed providers are a post-MVP feature.

If implemented, custom providers must default to conservative rules:

- HTTPS only;
- no DOM access or JavaScript injection;
- no cookie or token access;
- unrelated origins open in the system browser;
- privileged permissions denied until explicitly approved;
- status shown as `Experimental` or `Community Tested`, never `Verified` by default.

---

## 8. Milestone 0 — Provider Compatibility Lab

Before building the polished MVP shell, the project must create a minimal WebView2 compatibility harness.

### 8.1 Harness scope

The harness contains:

- one native window;
- one WebView2 instance;
- provider selection;
- a fresh, disposable test profile option;
- navigation event diagnostics;
- popup event diagnostics;
- permission request diagnostics;
- renderer/process failure diagnostics;
- basic CPU, memory, and process-count observations.

Diagnostics must not record credentials, cookies, tokens, prompts, responses, DOM, request bodies, or sensitive query parameters.

### 8.2 Required test matrix

Each candidate provider must be tested for:

| Area | Test |
|---|---|
| Authentication | Email, phone, QR, Google, Apple, passkey, or other methods offered by the provider. |
| Session | Close and restart the application; verify whether login persists. |
| Conversation | Send a basic prompt, receive streaming output, stop generation, and start a new conversation. |
| History | Open and switch an existing provider-managed conversation when supported. |
| Files | Upload, drag and drop, download, and provider file preview when supported. |
| Clipboard | Copy and paste expected text content. |
| Permissions | Microphone, camera, notification, and other requests when applicable. |
| Popup | OAuth, help, account, preview, and other new-window behavior. |
| Navigation | Internal provider links remain usable; unrelated links leave the application. |
| Recovery | Offline behavior, renderer failure, reload, and session recovery. |
| Long conversation | Exercise a representative provider-hosted long conversation and verify unresponsive-renderer detection, reload, same-profile restart, and login preservation. |
| Resources | Active-normal, hidden-low-memory, warm, disposed, and recreated WebView measurements. |
| Purchase boundary | Known upgrade, billing, subscription, and checkout flows do not complete inside AI Dock. |

### 8.3 Authentication Gate

A provider cannot be `Verified` if a new user cannot complete a supported login method without:

- spoofing the User-Agent;
- extracting or importing browser cookies;
- intercepting credentials or tokens;
- bypassing an embedded-browser restriction;
- reverse engineering private authentication endpoints.

Gemini is a high-risk compatibility candidate because Google may block Google Account authentication in embedded WebViews. The project must test actual behavior and must not claim support in advance.

### 8.4 Gate outcome

Milestone 0 produces:

- a provider compatibility matrix;
- reproducible environment information;
- known limitations per provider;
- a decision on which providers enter the public MVP;
- baseline resource measurements;
- a go/no-go decision for the full Windows shell.

---

## 9. Functional requirements

### FR-01 — Single-instance application

- Only one primary AI Dock process may own the tray icon, global shortcut, and active WebView2 user data folder.
- Starting a second instance must activate the existing instance and exit safely.

### FR-02 — Global show/hide shortcut

- Proposed default: `Win + Shift + A`.
- The shortcut must show and focus the application when hidden.
- It must restore the window when minimized.
- Pressing it while the window is active may hide the window.
- Registration failure must be detected and explained to the user.
- Users must be able to change or disable the shortcut.
- The shortcut must be unregistered during clean shutdown.

Implementation may use `RegisterHotKey`, `WM_HOTKEY`, and `UnregisterHotKey` through Win32 interop.

`Esc` must not hide the application by default because provider web applications use it for menus, dialogs, uploads, and voice interfaces. Optional Escape-to-hide behavior may be considered later.

### FR-03 — Main window

- Show the active provider clearly.
- Provide minimal provider switching controls.
- Restore the last valid size and position.
- Recover gracefully when a saved monitor is disconnected.
- Support an optional always-on-top setting.
- Complex custom window chrome is not required for MVP.

### FR-04 — Provider switching

- `Ctrl + 1`, `Ctrl + 2`, and subsequent numeric shortcuts select pinned providers.
- The selector must scale beyond three providers.
- Users may pin or reorder providers after MVP if needed.
- A disabled or external-only provider must communicate its state before navigation.

### FR-05 — Lazy WebView creation

- No provider WebView is created until first use.
- The native shell must become responsive before WebView initialization completes.
- A launch-on-startup instance that remains hidden in the tray must not create a WebView until the user opens a provider.
- The selected startup provider is the only provider initialized during a normal visible startup.
- WebViews must share a compatible `CoreWebView2Environment` and one application-specific user data folder where appropriate.
- Each provider should use a distinct WebView2 profile under that user data folder so its cookies, cache, permissions, recovery, and data reset remain isolated from other providers.
- AI Dock must never reuse or modify the user's normal Edge or Chrome profile folder.

### FR-06 — Bounded WebView lifecycle

Default `Balanced` behavior:

```text
Configured providers   many
Active WebView         1
Warm/recent WebView    up to 1
Maximum live WebViews  2
All others             disposed; persistent website data retained
```

Lifecycle states:

```text
NotCreated → ActiveNormal → HiddenLowMemory → WarmHidden → Disposed
```

- The active provider uses normal memory priority while visible.
- Hiding AI Dock must not immediately suspend the active provider. The default behavior is to request a low memory target while allowing provider scripts and network activity to continue, so a response can finish while the window is hidden.
- Returning the active provider to the foreground must restore its normal memory target before interaction.
- A provider that the user switches away from may remain warm for a measured grace period, after which it is disposed if it exceeds the live-view budget.
- `TrySuspendAsync()` is an alternative lifecycle strategy, not an additional layer to mix with `MemoryUsageTargetLevel` on the same WebView. It may be enabled only after focused testing proves that it does not disrupt streaming, upload, voice, research, or media workflows.
- Suspension and low-memory requests are best-effort and must not be described as full memory release.
- Disposing a WebView must not delete its persistent profile.
- Future settings may expose `Low Memory`, `Balanced`, and `Fast Switching` modes.

### FR-07 — Persistent sessions

- WebView2 website data must use an application-specific persistent User Data Folder.
- The folder must be stored in an appropriate writable local application-data location.
- Login state should persist when the provider and WebView2 support it.
- AI Dock must not read session cookies or tokens through application code.
- Settings must provide a clear action to clear provider browsing data or reset the whole AI Dock web profile.
- Clearing data must require confirmation and must occur only after relevant WebViews are closed.

### FR-08 — System tray

Tray actions:

```text
Open AI Dock
Open pinned provider
Settings
Exit
```

- Closing the main window hides it by default when tray mode is enabled.
- `Exit` must terminate the process and release WebViews, hotkeys, and tray resources.
- Tray behavior must be tested across Explorer restart, DPI changes, light/dark themes, and multi-display setups.
- A lightweight maintained dependency such as H.NotifyIcon.WinUI may be used after license and lifecycle review.

### FR-09 — Navigation boundary

The application must distinguish:

1. provider application navigation;
2. required authentication and provider popup navigation;
3. ordinary external links;
4. known purchase and billing flows;
5. unsupported or unsafe schemes.

Rules:

- Provider application origins remain in AI Dock.
- Authentication origins are allowed only when verified and required by the provider flow.
- Ordinary unrelated links open in the system browser.
- Known purchase flows follow FR-10.
- Unknown custom schemes are denied by default.
- Certificate errors must not be ignored or bypassed.
- Host validation must use parsed URI origin and exact domain/subdomain boundaries, not string `Contains` checks.
- `NavigationStarting`, `CoreWebView2Frame.NavigationStarting`, and `NewWindowRequested` must be handled.

### FR-10 — No in-app provider purchases

AI Dock does not sell, process, manage, or receive commission from AI provider subscriptions.

Known subscription, billing, recharge, membership, and checkout flows must not complete inside AI Dock.

The application should:

- cancel known purchase navigation in the main frame;
- handle and cancel known purchase popups;
- cancel known payment iframe navigation where detectable;
- maintain provider-specific blocked hosts and path patterns;
- show a native disclosure before optionally opening the provider's official root or account site in the system browser;
- avoid forwarding checkout session IDs, affiliate parameters, payment tokens, or sensitive query strings;
- never read payment forms or store payment information.

Suggested disclosure:

> AI Dock does not provide or process subscriptions. You are leaving AI Dock and visiting the provider's official website. Purchases, billing, cancellations, and refunds are handled directly by that provider.

This control is best-effort. AI Dock must not claim that it can prevent a user from purchasing through other browsers, devices, or provider-controlled same-origin interfaces.

### FR-11 — Permissions

- Permission requests must be evaluated per provider and permission type.
- Camera, microphone, geolocation, notifications, and other privileged permissions must not be silently granted.
- The application must show the requesting provider and permission clearly.
- Decisions may be remembered only when the user explicitly chooses that option.
- Provider features may be marked limited if safe permission handling cannot be verified.

### FR-12 — Uploads and downloads

- Standard provider-controlled file upload must work where WebView2 supports it.
- Downloads must use an explicit user-selected or clearly communicated destination.
- Executable and uncommon file downloads require additional caution.
- Download filenames and target paths must be sanitized and handled by WebView2/Windows APIs without arbitrary application execution.
- AI Dock must never automatically open a downloaded executable.

### FR-13 — WebView failure recovery

- Handle WebView initialization failures, missing runtime, process failure, renderer crash, renderer unresponsiveness, out-of-memory termination, and navigation errors.
- Subscribe to `CoreWebView2.ProcessFailed` and distinguish at minimum `RenderProcessUnresponsive`, `RenderProcessExited`, `BrowserProcessExited`, an `OutOfMemory` failure reason, and auto-recoverable GPU or utility process failures.
- A first unresponsive-renderer event must not automatically destroy the page. Show a non-blocking status and allow the user to keep waiting.
- Repeated unresponsive-renderer events must offer `Keep waiting`, `Reload page`, and `Restart provider` actions.
- `Reload page` calls the normal WebView reload path and preserves the provider profile.
- `Restart provider` closes and recreates only that provider's WebView using the same profile, then navigates back to the last safe in-memory provider URL.
- The recovery URL may be retained transiently in memory but must not be written to diagnostics or persisted with sensitive query parameters.
- A renderer exit may trigger one bounded reload attempt. If recovery fails, recreate that provider view.
- A browser-process exit requires coordinated recreation of affected WebViews that shared the environment.
- An out-of-memory failure must first release inactive provider views and must not enter an automatic reload loop.
- Recovery must not silently clear browsing data, delete a profile, or sign the user out.
- Provide a separate, explicitly confirmed `Reset provider data` action only for corrupted site data or login-loop recovery.
- Explain that reload or restart may interrupt an active response, upload, voice session, or other in-progress provider operation.

### FR-14 — Settings

MVP settings:

```text
Default provider
Global shortcut
Launch on startup
Close to tray
Always on top
Window size and position
Memory mode, if implemented
Provider browsing-data reset
Application profile reset
```

Autofill policy:

- General form autofill should be disabled by default.
- Password autosave should remain disabled.
- Disabling autofill is a privacy control, not a guarantee that provider or manually entered payment data cannot be used.

---

## 10. Security and privacy requirements

### 10.1 Embedded website as a security boundary

Treat every provider website as untrusted web content relative to native application privileges.

The native application must not expose host objects, filesystem capabilities, terminal execution, or privileged native commands to provider pages.

Unless a documented feature requires otherwise:

```text
AreHostObjectsAllowed = false
IsWebMessageEnabled = false
AreDevToolsEnabled = false in public release builds
IsGeneralAutofillEnabled = false
IsPasswordAutosaveEnabled = false
```

JavaScript must remain enabled because provider sites require it, but AI Dock must not add custom scripts to those pages.

### 10.2 Data the application may know

```text
Provider ID
Current top-level origin or sanitized URL category
Compatibility status
Window state
Application settings
Permission decisions
WebView process health
Coarse performance measurements
```

### 10.3 Data the application must not collect

```text
Prompts
AI responses
Conversation history
Uploaded file content
Passwords
Session tokens
Cookies
Payment information
Checkout session identifiers
OAuth codes
Full sensitive URLs
DOM or rendered page content
```

### 10.4 Local storage

Only these categories may be stored:

- application configuration;
- window and shortcut preferences;
- provider definitions and compatibility metadata;
- WebView2-managed cookies, caches, and site storage inside the application-specific UDF;
- minimal non-sensitive diagnostic logs when enabled.

The application must document that WebView2 site data can contain authenticated sessions even though AI Dock native code does not inspect them.

### 10.5 Logging

Production logs must use sanitized event categories rather than sensitive URLs.

Allowed example:

```text
Provider=gemini Event=AuthNavigationBlocked Rule=EmbeddedAuthRestriction
```

Disallowed example:

```text
FullUrl=https://...token=...&session=...
Prompt=...
Cookie=...
```

### 10.6 Product and trademark disclosure

The application, website, README, and store listing must state that AI Dock is independent and unofficial and is not affiliated with, endorsed by, sponsored by, or supported by listed AI providers.

Provider names and logos must be reviewed for trademark and brand-guideline compliance before public distribution.

---

## 11. Performance requirements

AI Dock is designed to feel like an operating-system utility: the native shell must start quickly, a warm shortcut invocation must feel immediate, provider switching must remain bounded, and long-running web content must not cause unbounded process or memory growth.

WebView2 uses the Edge browser process model, so AI Dock must report measured behavior rather than asserting that WebView2 is inherently lightweight. Provider page complexity and network latency must be reported separately from AI Dock-owned startup and lifecycle overhead.

### 11.1 Performance architecture

- Construct and show the native shell before awaiting provider navigation.
- Never call `.Result`, `.Wait()`, or otherwise block the UI thread on WebView2 asynchronous operations.
- Launch-on-startup in hidden tray mode must initialize only native lifecycle services, settings, tray, and hotkey registration. It must not eagerly start provider renderers.
- Lazy-load the first provider after the user requests it and show clear loading state without freezing native controls.
- Keep at most the configured number of live WebViews; the default target remains two.
- Preserve login through provider profiles while disposing renderer state that is no longer needed.
- Do not automatically clear HTTP cache, cookies, IndexedDB, or local storage as a performance optimization.
- Treat provider server history as the authoritative conversation source; AI Dock must not create a second local conversation cache.
- Use low-memory priority for the hidden active provider when supported and tested, then restore normal priority when visible.
- Dispose inactive providers after a measured grace period rather than allowing every previously visited provider to remain resident.

### 11.2 Long-conversation performance boundary

Long conversations may create large DOM trees, JavaScript heaps, Markdown rendering work, code highlighting, images, and provider-specific application state inside the renderer. AI Dock does not own this content and must not scrape, truncate, virtualize, or rewrite it.

When a long conversation becomes slow or unresponsive, the supported recovery ladder is:

```text
Wait
  ↓
Reload page
  ↓
Restart provider WebView with the same profile
  ↓
Open in system browser
  ↓
Reset provider data only when site data is believed to be corrupted
```

The application must clearly distinguish:

| Action | Effect | Login impact |
|---|---|---|
| `Reload page` | Rebuilds the current document and requests provider-hosted state again. | Normally preserved. |
| `Restart provider` | Disposes the renderer and JavaScript heap, recreates the WebView, and reuses the provider profile. | Preserved where the provider session remains valid. |
| `Reset provider data` | Deletes that provider's local website data after confirmation. | Signs the user out. |

AI Dock must not claim that reload will preserve an unfinished response. A completed response stored by the provider may reappear after reload, but streaming, upload, voice, and other in-progress operations may be interrupted.

### 11.3 Metrics

- cold shell startup time;
- cold shell time-to-visible and time-to-native-interactive;
- warm global-shortcut-to-visible latency;
- hidden launch-on-startup resource usage before any provider is opened;
- provider switch-to-interactive latency;
- first provider initialization time, reported separately from provider network load;
- hidden idle CPU usage;
- active, hidden-low-memory, and disposed working set;
- native shell working-set overhead separated from provider renderer processes where measurable;
- WebView2 process count;
- memory after visiting multiple providers;
- memory after low-memory transition and disposal;
- memory recovery after cycling through the initial provider matrix;
- frequency and duration of renderer-unresponsive events;
- time to acknowledge reload or restart commands;
- recovery time after renderer or browser process failure.

### 11.4 Baselines

Compare against:

- the same provider installed as an Edge site app;
- official ChatGPT and Claude desktop clients where available;
- a normal Edge window containing equivalent provider tabs.

Comparisons must use the same provider account, conversation, network conditions, hardware power mode, and equivalent visible/hidden state where practical. Results must preserve earlier runs and disclose material differences rather than selecting only the most favorable rerun.

### 11.5 Provisional performance targets

These targets guide implementation and must be calibrated against Milestone 0 reference hardware before becoming public promises:

| Scenario | Provisional target |
|---|---|
| Warm global shortcut reveal | Window visible and focus requested within 200 ms at p95. |
| Native shell cold start | Native controls visible and responsive within 1 second at p95 on reference hardware, excluding first provider network load. |
| Warm provider switch | Existing warm view selected and visible within 250 ms at p95. |
| Hidden startup before provider use | No provider renderer process is created. |
| Hidden idle CPU | At or below 1% average after stabilization, excluding active provider streaming, upload, voice, research, or media work. |
| Live WebView count | Never exceeds the configured budget during normal switching. |
| Multi-provider memory recovery | After inactive views are disposed and stabilization completes, memory returns close to the footprint of the active view plus the configured warm-view budget; the acceptable variance is set from M0 evidence. |
| Recovery command response | Native UI acknowledges reload or restart within 250 ms; provider page usability remains network- and provider-dependent. |

Targets must be measured on at least one reference mid-range Windows 11 device. Where available, repeat warm-path measurements after system restart and under a realistic background workload.

### 11.6 Initial success criteria

- Warm show/hide feels immediate and reliably focuses the window.
- Cold startup never waits synchronously for provider content before presenting responsive native UI.
- Starting hidden with Windows does not initialize provider WebViews.
- Hidden idle CPU remains negligible in normal non-streaming use.
- Visiting many configured providers does not create an unbounded number of live WebViews.
- Reopening a disposed provider preserves login when the provider supports persistent WebView sessions.
- Hiding the active provider does not immediately suspend an answer that may still be generating.
- Long-conversation renderer unresponsiveness produces a user-controlled recovery path without reading conversation content.
- Restarting one provider releases its old renderer state without resetting other providers.
- No significant UI-thread blocking or deadlock occurs during WebView creation and navigation.

Exact numeric targets must be established from Milestone 0 measurements before public performance claims.

---

## 12. Accessibility and UX requirements

- Full native navigation must be usable by keyboard.
- Provider selection and compatibility status must have accessible names.
- Focus must move predictably between the native selector and WebView.
- Light and dark themes must remain readable.
- The application must not hide critical provider warnings or authentication errors.
- Error messages must explain whether the problem belongs to AI Dock, WebView2, the network, or the provider.
- Provider-specific limitations must be visible without requiring users to inspect GitHub issues.

---

## 13. Distribution and website strategy

### 13.1 Windows distribution

Primary stable distribution:

```text
Microsoft Store
└── MSIX package
```

Benefits required from the primary channel:

- trusted installation;
- clean uninstall;
- Store-managed updates;
- package identity;
- reduced SmartScreen and certificate friction;
- appropriate Windows App SDK dependency handling.

Secondary distribution:

```text
GitHub Releases
├── release notes
├── signed release artifact when publicly distributed
├── SHA-256 checksum
└── known limitations
```

An unsigned public `Setup.exe` must not be the only stable distribution path. If a traditional EXE installer is later offered, it must have an update strategy and appropriate Authenticode signing.

### 13.2 Release phases

```text
Compatibility Lab
    Internal/test builds only

Alpha
    GitHub prerelease for informed testers

Public Beta
    Independent website + GitHub release

Stable
    Microsoft Store MSIX primary + GitHub fallback
```

### 13.3 Independent website

The website must be independent from SkyLink in repository, deployment, branding, domain, privacy policy, and release lifecycle.

The website is not required before the compatibility Gate passes. Before Public Beta, create a minimal static site deployed independently, with Vercel as an acceptable host.

Required routes:

```text
/
/download
/providers
/privacy
/security
/changelog
/support
```

The website should link to Microsoft Store and GitHub Releases rather than acting as the primary large-binary CDN.

The website must not require a backend, application account, database, or analytics service for MVP.

---

## 14. Open-source and funding model

### Open source

- The source repository is public by the first stable release.
- Build instructions and architecture boundaries must be documented.
- Releases should be traceable to version tags.
- Security reports need a private reporting path.
- Provider compatibility contributions should include test environment and evidence, not only a URL definition.

The repository uses the Apache License 2.0. Its notices and obligations must be preserved, and all bundled dependencies and assets must remain license-compatible.

### Buy Me a Coffee

Buy Me a Coffee is an optional project-support mechanism, not an AI subscription and not payment for access to provider services.

- The support link opens in the system browser.
- It must be clearly labelled as support for independent open-source development.
- It must not be confused with provider subscriptions.
- AI Dock must not receive commissions from provider purchases.
- Core privacy or security functionality must not be gated behind a donation.

Suggested disclosure:

> Optional donations support development of the independent AI Dock open-source project. They are unrelated to subscriptions sold by AI providers.

---

## 15. Future macOS direction

macOS is explicitly out of scope for the Windows MVP, but Windows code must avoid unnecessary coupling that would prevent a later native Mac shell.

Recommended future architecture:

```text
Shared product concepts and file formats
├── Provider definitions
├── Navigation categories
├── Purchase policies
├── Compatibility schema
└── Settings schema where portable

Windows shell
├── C# / .NET
├── WinUI 3
├── WebView2
├── Win32 global hotkey
└── Windows tray

macOS shell
├── SwiftUI / AppKit
├── WKWebView
├── macOS global shortcut
└── Menu bar item
```

The project should not introduce .NET MAUI, Electron, or another cross-platform framework during the Windows MVP solely to anticipate macOS.

Provider compatibility must be retested on macOS because WKWebView authentication and web behavior can differ from WebView2.

---

## 16. Milestones

### M0 — Compatibility Lab

Deliverables:

- minimal WebView2 test harness;
- eight-provider test matrix;
- authentication and session evidence;
- resource baselines;
- provider status decisions;
- go/no-go decision.

### M1 — Windows shell

Deliverables:

- WinUI 3 application shell;
- single-instance behavior;
- one provider loaded through WebView2;
- global shortcut;
- close-to-tray and true exit;
- persistent application-specific WebView profile;
- basic failure recovery.

### M2 — Multi-provider MVP

Deliverables:

- data-driven provider registry;
- provider selector and keyboard shortcuts;
- bounded two-WebView lifecycle;
- compatibility status display;
- safe navigation and popup handling;
- permission handling;
- provider profile reset.

### M3 — Security and commerce boundary

Deliverables:

- strict origin validation;
- external browser handoff;
- known purchase-flow blocking;
- sanitized diagnostics;
- privacy and unofficial-product disclosures;
- focused security review.

### M4 — Public Beta

Deliverables:

- packaged beta build;
- GitHub Releases workflow;
- independent minimal website;
- privacy, security, and provider-status pages;
- issue and security-reporting templates;
- BMC support link with correct disclosure.

### M5 — Stable Windows release

Deliverables:

- Microsoft Store MSIX submission;
- release signing and provenance checks;
- stable compatibility matrix;
- update and rollback process;
- performance comparison report;
- end-user documentation.

### M6 — macOS feasibility

Begins only after Windows product validation.

Deliverables:

- WKWebView provider compatibility matrix;
- native menu bar and shortcut prototype;
- signing, notarization, and distribution plan;
- decision on Mac App Store versus signed direct download.

---

## 17. MVP acceptance criteria

The Windows MVP is acceptable only when:

1. At least two providers pass `Verified` criteria, or the product decision explicitly accepts a smaller verified set.
2. The global shortcut reliably shows, focuses, and hides/restores the application.
3. The application remains single-instance.
4. Provider login survives restart where supported by that provider.
5. No more than the configured maximum live WebViews remain after visiting many providers.
6. External links do not turn AI Dock into a general-purpose browser.
7. Known purchase and subscription flows cannot complete inside AI Dock during the tested matrix.
8. The native application does not inspect conversation content, credentials, cookies, tokens, DOM, or payment information.
9. WebView initialization and renderer failures have a recoverable user experience.
10. Repeated renderer-unresponsive events expose wait, reload, and provider-restart controls without inspecting conversation content.
11. Reloading or restarting a provider normally preserves its authenticated profile, while resetting provider data is a separate confirmed action that signs the user out.
12. Hiding the current provider does not immediately suspend possible in-progress generation, upload, voice, research, or media work.
13. A hidden launch-on-startup instance creates no provider renderer until requested.
14. Resetting one provider does not delete unrelated provider or application files.
15. Exit releases the tray icon, hotkey, WebViews, and process.
16. Compatibility and limitations are visible to users.
17. Distribution artifacts and documentation clearly identify the project as independent and unofficial.
18. Performance claims are backed by recorded comparisons against the defined baselines.

---

## 18. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Embedded authentication is blocked | Provider unusable for new users | Test before claiming support; never spoof UA; mark Limited, Experimental, or ExternalOnly. |
| Provider changes website behavior | Sudden regression | Versioned compatibility matrix, provider-specific rules, rapid patch process. |
| Too many WebViews consume memory | Product contradicts lightweight positioning | Lazy creation, a strict live-view budget, low-memory priority for the hidden active view, disposal of inactive views, and measurement. |
| Long conversations make a provider renderer unresponsive | Answer exists server-side but the embedded page stops updating | Detect renderer unresponsiveness; offer wait, reload, and same-profile provider restart; do not duplicate or inspect conversations. |
| Hidden-window suspension interrupts an in-progress answer | User expects work to continue while AI Dock is hidden | Use tested low-memory priority for the active hidden provider; dispose inactive views after the grace period, and enable suspension only if focused tests prove it safe. |
| External navigation becomes general browsing | Increased attack surface and scope | Strict origin policies and system-browser handoff. |
| Purchase occurs inside embedded site | User confusion or support dispute | Block known flows, show disclosure, open only official sites externally. |
| Session data is sensitive | Account exposure on a shared device | Application-specific UDF, profile reset, clear documentation, no native cookie access. |
| WinUI tray/hotkey lifecycle bugs | Duplicate icons or lost shortcut | Single instance, tested Win32 lifecycle, maintained tray dependency or focused native implementation. |
| Unsigned direct installer triggers warnings | Low trust and poor installation conversion | Store MSIX primary; sign public direct artifacts. |
| Provider trademarks imply affiliation | Legal and trust risk | Unofficial-product disclosure and brand-guideline review. |
| Cross-platform abstraction slows MVP | Delayed validation | Windows-native MVP; share schemas and policies, not premature UI framework abstractions. |

---

## 19. Open decisions

These decisions remain open and must be resolved with evidence:

1. Final public product name and domain.
2. Exact providers that pass the M0 compatibility Gate.
3. Whether the default live-WebView limit is one or two after measurement.
4. Exact shortcut after conflict testing.
5. H.NotifyIcon.WinUI versus a small native notification-area implementation.
6. MSIX framework-dependent versus self-contained packaging details.
7. Code-signing approach for GitHub release artifacts.
8. Whether provider icons can be bundled under applicable brand guidelines.
9. Whether any optional, strictly opt-in anonymous diagnostics are justified after MVP. Default remains no analytics backend.

---

## 20. Initial implementation instruction

The first implementation task is **not** the complete polished application.

Create Milestone 0: a deliberately small Windows 11 WebView2 compatibility harness using C#, .NET, WinUI 3, Windows App SDK, and WebView2.

Before adding full tray, branding, website, or public installers:

1. Test the initial eight providers.
2. Document authentication and session limitations.
3. Verify navigation, popup, permission, file, and failure behavior.
4. Measure resource usage.
5. Verify known purchase flows are prevented from completing inside the harness.
6. Produce the compatibility matrix and recommend the verified MVP provider set.

Do not introduce Electron, Node.js, a backend, AI APIs, a database, DOM automation, User-Agent spoofing, cookie import, or unnecessary dependencies.

The primary engineering objective remains:

> Build a reliable, lightweight, privacy-respecting native desktop shell for compatible official AI web applications.

---

## 21. Reference documentation

- [Windows App SDK and WinUI 3](https://learn.microsoft.com/en-us/windows/apps/)
- [WebView2 in WinUI 3](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/webview2)
- [WebView2 user data folders](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder)
- [WebView2 navigation events](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/navigation-events)
- [WebView2 security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
- [WebView2 performance guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- [WebView2 process failure and unresponsive-renderer handling](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-related-events)
- [WebView2 memory usage target level](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.memoryusagetargetlevel)
- [WebView2 Runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [Windows app packaging overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/)
- [Windows distribution paths](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path)
- [Google OAuth 2.0 policies](https://developers.google.com/identity/protocols/oauth2/policies)
- [Google supported-browser sign-in guidance](https://support.google.com/accounts/answer/7675428)
- [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)
