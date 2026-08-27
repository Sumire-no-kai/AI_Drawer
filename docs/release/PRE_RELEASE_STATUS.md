# Public Beta pre-release status

Last verified: 2026-08-27

This is the durable current-state summary for the first AI Drawer Public Beta. It complements the chronological development log and the normative release checklist. A source change, passing build, runtime result, package result, device result, and publication result are separate evidence Gates.

## Current baseline

- `master` commit: `0191ee5e16bede4f0da9dbefd0b9d3a6713034c9`
- Merged work: PR #18, reviewed head `cd925b2b2d49bb0c3cc7595f51dabe75bd9d75a6`
- Post-merge CI: run `33037886187`; x64, x86, ARM64, formatting, and compatibility/privacy-safe policy jobs passed
- Public artifact: none; inspected candidates remain unsigned internal evidence and must not be distributed to end users
- Planning estimate only: application implementation about 90%, code-freeze readiness about 85%, Public Beta readiness about 60–65%. These percentages do not override an unchecked release Gate.

## First Beta platform scope

| Target | Public support intent | Current evidence |
| --- | --- | --- |
| Windows 11 x64 | Included | Current AMD64 host is available and reports build `10.0.26200.9168`; final merged-source GUI, accessibility-setting, and package acceptance remain open. |
| Windows 10 x64 | Included | Matching device or controlled VM acceptance remains open. Only versions actually tested may be named in release notes. |
| Windows 11 ARM64 | Included | Cross-build and package inspection pass; matching-device runtime, WebView2, tray, shortcut, startup, install, and uninstall acceptance remain open. |
| Windows x86 | Not included | CI/build inspection only; it is not a first-Beta runtime support claim. |

Do not claim all serviced Windows 10 editions, LTSC 2019, or a broad Windows range unless those exact environments pass. Cross-architecture compilation is never device-runtime evidence.

## What can be completed on the current host

1. Align a clean checkout to the merged `master` commit and repeat Release builds, maintained tests, formatting, dependency/license review, and a final repository security scan.
2. Run the final fast and five-minute no-account GUI acceptance, including cold start, onboarding input, single instance, four-workspace pressure, Keep active, grace-period convergence, workspace restoration, renderer/GPU/browser recovery, cache/reset actions, close-to-tray, shortcut restoration, exact Exit, and post-exit process/profile residue.
3. Record cold-start, memory, CPU, WebView2 process count, live-view peak, and steady-state convergence without making unmeasured browser-comparison claims.
4. Exercise Windows 11 x64 light/dark, minimum-window, keyboard/focus order, and app-level UI Automation. Narrator, High Contrast, reduced-motion/transparency, 150%/200% DPI, and text scaling require temporary visible system-setting changes and human visual confirmation.
5. Manually confirm the fixed BMC and Microsoft Forms destinations open in the system browser without appending provider, account, application, build, query, or fragment data where the applicable policy requires stripping.

## Gates that need accounts, another environment, or owner action

### Provider/account evidence

- Complete the compatibility matrix for Gemini, ChatGPT, Claude, Grok, DeepSeek, Doubao, Qwen, GLM, and Microsoft Copilot Personal.
- Test supported login, authentication popups, session persistence, basic conversation, history, uploads/downloads, permissions, external navigation, recovery, cache clear, provider reset, and reset-all scope.
- Trigger but never complete known purchase/upgrade routes. Do not enter payment data. Add exact privacy-safe purchase rules for non-Gemini providers and narrow Grok's `x.com` authentication boundary from observed evidence rather than guessed paths.
- Keep unverified providers `Experimental` or `Limited`; inclusion in the selector is not a `Verified` compatibility claim.

### Device and accessibility evidence

- Run the agreed Windows 10 x64 and Windows 11 ARM64 matrices on matching environments.
- Record exact OS build, architecture, WebView2 Runtime, display scale, text scale, theme, accessibility settings, package version, and pass/fail evidence for each run.

### Package and release evidence

- Use a clean package-test user or machine for exact 1.0.0.0 install, 1.0.0.1 update, startup registration, rollback behavior, true exit, uninstall, and data-retention decisions. Do not delete the unidentified registration that caused `0x80073CFB` on the current host.
- Approve the public product name, package identity, publisher, version scheme, framework-dependent versus self-contained model, Store/direct-distribution route, and signing/provenance process.
- Verify Microsoft Forms ownership, notifications, retention, privacy wording, and monitoring. Publish consistent privacy, security, provider-status, independence, unofficial-product, session-data, purchase-boundary, support, and changelog surfaces.
- Test the exact signed bytes intended for publication, record certificate chain and SHA-256, create release notes and a source tag, publish first as a prerelease, then download and repeat checksum/install/launch/uninstall validation.

## Open security findings

The last Standard scan found no backdoor, covert telemetry, sensitive page-to-native collection, arbitrary command execution, automatic download execution, hardcoded secret, or unexpected package capability. It did not close two Low provider-policy findings:

1. Non-Gemini providers still lack exact observed purchase-route rules, so a same-origin checkout path may remain embedded.
2. Grok currently permits the full exact `x.com` application origin pending privacy-safe evidence of the minimum authentication route.

Both require focused account/runtime evidence and regression vectors for top-level, frame, and popup navigation. Native warnings and strict origin checks mitigate but do not close them.

## Final go/no-go rule

Public Beta is **No-Go** while any item in `BETA_RELEASE_CHECKLIST.md` remains unchecked, unless the PRD and release scope are explicitly revised and the omitted behavior is documented as a user-visible Beta limitation. The exact published bytes—not an earlier local build—must pass the final package and supported-environment checks.
