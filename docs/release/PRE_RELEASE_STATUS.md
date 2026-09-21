# Public Beta pre-release status

Last verified: 2026-09-21

This is the durable current-state summary for the first AI Drawer Public Beta. It complements the chronological development log and the normative release checklist. A source change, passing build, runtime result, package result, device result, and publication result are separate evidence Gates.

The retained-tab and split-view redesign supersedes the earlier automatic-disposal lifecycle. Its Windows 11 x64 no-account source, UI, five-page, five-minute, recovery, and guest-provider acceptance is recorded in [HOST_ACCEPTANCE_2026-09-21.md](../testing/HOST_ACCEPTANCE_2026-09-21.md). Provider-account, additional-device, accessibility, and signed-package Gates remain separate.

## Current baseline

- Acceptance implementation baseline: `a7d9b5e`, based on `master` `f21c444`; retained-tab delivery was merged through PR #21 before this acceptance-fix work.
- Public artifact: none; inspected candidates remain unsigned internal evidence and must not be distributed to end users
- Current-host retained-tab no-account acceptance is complete and recorded in `docs/testing/HOST_ACCEPTANCE_2026-09-21.md`. The earlier M4 baseline remains recorded in `docs/testing/HOST_ACCEPTANCE_2026-08-27.md`. Planning percentages are intentionally omitted because they do not substitute for release Gates.

## First Beta platform scope

| Target | Public support intent | Current evidence |
| --- | --- | --- |
| Windows 11 x64 | Included | Retained-tab source, five-page runtime, real five-minute retention/recovery, guest-provider visual checks, and Light/150% DPI UI Automation pass on build `10.0.26200.9457` with WebView2 `153.0.4234.48`. Earlier temporary Dark/200% text/reduced-motion/transparency-off evidence remains tied to the 2026-08-27 baseline. Provider-account, live High Contrast/Narrator/200% display DPI, and signed-package acceptance remain open. |
| Windows 10 x64 | Included | Matching device or controlled VM acceptance remains open. Only versions actually tested may be named in release notes. |
| Windows 11 ARM64 | Included | Cross-build and package inspection pass; matching-device runtime, WebView2, tray, shortcut, startup, install, and uninstall acceptance remain open. |
| Windows x86 | Not included | CI/build inspection only; it is not a first-Beta runtime support claim. |

Do not claim all serviced Windows 10 editions, LTSC 2019, or a broad Windows range unless those exact environments pass. Cross-architecture compilation is never device-runtime evidence.

## Completed on the current host

1. Merged-source builds, maintained tests, formatting, dependency/license review, unsigned candidate inspection, and a final repository Standard security scan are complete.
2. Fast and five-minute no-account GUI acceptance is complete for the retained-tab design, including five live pages, same-provider creation, rename, physical drag ordering, search, shortcuts, release/recreation, Renderer/GPU/Browser recovery, cache/reset APIs, tray, shortcut, second launch, exact Exit, and zero process/profile residue.
3. One-host resource measurements are recorded without extrapolation: 10 WebView2 processes remained present across the five-minute interval; the exact working-set values are in the 2026-09-21 host record.
4. Light/150% DPI, default and narrow split layouts, keyboard focus, release confirmation, dismissible status warnings, and app-level UI Automation pass for the current branch. The earlier temporary Dark/200% text/reduced-motion/transparency-off run has not been repeated against the retained-tab redesign.
5. Fixed BMC and Forms URL contracts are source- and policy-tested. The user-visible system-browser launch remains a final manual interaction check so automated acceptance does not open external pages or inspect an existing browser profile.

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

Standard scan `50e49bc8-acaa-4684-9639-5fbd29f91623` at `c243c16` covered 108/108 tracked-file receipts and found no backdoor, covert telemetry, sensitive page-to-native collection, arbitrary command execution, automatic download execution, hardcoded secret/private key, or unexpected package capability. It did not close two Low provider-policy findings:

1. Non-Gemini providers still lack exact observed purchase-route rules, so a same-origin checkout path may remain embedded.
2. Grok currently permits the full exact `x.com` application origin pending privacy-safe evidence of the minimum authentication route.

Both require focused account/runtime evidence and regression vectors for top-level, frame, and popup navigation. Native warnings and strict origin checks mitigate but do not close them.

## Final go/no-go rule

Public Beta is **No-Go** while any item in `BETA_RELEASE_CHECKLIST.md` remains unchecked, unless the PRD and release scope are explicitly revised and the omitted behavior is documented as a user-visible Beta limitation. The exact published bytes—not an earlier local build—must pass the final package and supported-environment checks.
