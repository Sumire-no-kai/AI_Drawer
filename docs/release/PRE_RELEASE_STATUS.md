# Public Beta pre-release status

Last verified: 2026-08-30

This is the durable current-state summary for the first AI Drawer Public Beta. It complements the chronological development log and the normative release checklist. A source change, passing build, runtime result, package result, device result, and publication result are separate evidence Gates.

## Current baseline

- `master` commit: `a4bc03a1d15041aa785513bc0bdc67d172a1ae94`
- Latest merged work: PR #19, reviewed head `bb7699a`; the merge contains the release-status refresh and stabilized acceptance assertions
- Post-merge CI: run `33064037373`; x64, x86, ARM64, formatting, and compatibility/privacy-safe policy jobs all passed on the exact master commit
- Public artifact: none; inspected candidates remain unsigned internal evidence and must not be distributed to end users
- Current-host no-account source/runtime acceptance is complete and recorded in `docs/testing/HOST_ACCEPTANCE_2026-08-27.md`. The current release-hardening branch adds external-link launch evidence, explicit package-mode comparison, a post-build verifier, and draft public-surface copy; it is not merged or public evidence until its PR and CI complete. Planning estimate only: application implementation about 94%, code-freeze readiness about 90%, Public Beta readiness about 70–75%. These percentages do not override an unchecked release Gate.

## First Beta platform scope

| Target | Public support intent | Current evidence |
| --- | --- | --- |
| Windows 11 x64 | Included | Source, no-account runtime, real five-minute resource/recovery, Light/150% DPI UI Automation, and temporary Dark/200% text/reduced-motion/transparency-off checks pass on build `10.0.26200.9168` with WebView2 `151.0.4129.107`. Provider-account, live High Contrast/Narrator/200% display DPI, and signed-package acceptance remain open. |
| Windows 10 x64 | Included | Matching device or controlled VM acceptance remains open. Only versions actually tested may be named in release notes. |
| Windows 11 ARM64 | Included | Cross-build and package inspection pass; matching-device runtime, WebView2, tray, shortcut, startup, install, and uninstall acceptance remain open. |
| Windows x86 | Not included | CI/build inspection only; it is not a first-Beta runtime support claim. |

Do not claim all serviced Windows 10 editions, LTSC 2019, or a broad Windows range unless those exact environments pass. Cross-architecture compilation is never device-runtime evidence.

## Completed on the current host

1. Merged-source builds, maintained tests, formatting, dependency/license review, unsigned candidate inspection, and a final repository Standard security scan are complete.
2. Fast and five-minute no-account GUI acceptance is complete, including cold start, single instance, four-workspace pressure, Keep active, grace-period convergence, workspace restoration, Renderer/GPU/Browser recovery, cache/reset APIs, tray, shortcut, second launch, exact Exit, and zero process/profile residue.
3. One-host cold-start, memory, and WebView2 process measurements are recorded without extrapolation: 8 processes at burst, 7 after grace expiry, and 6 at stable pre-fault state.
4. Light/150% DPI, minimum-window, keyboard/focus, and app-level UI Automation pass. The same suite exits successfully under temporary Dark, 200% text, reduced-motion, and transparency-off settings, with the original settings restored afterward.
5. Fixed BMC and Forms URL contracts pass source, record-only UI Automation, and one explicit live system-browser launch run from Home, Feedback & Support, and Settings. The live run opened two fixed BMC and two fixed Forms destinations and recorded only Windows' successful launch result; it did not inspect browser tabs, accounts, or page content.
6. Framework-dependent and self-contained x64/ARM64 candidates build with explicit dependency properties and pass independent reinspection. The size/dependency comparison is recorded in `PACKAGING_MODE_EVIDENCE_2026-08-30.md`; it informs but does not decide the public packaging model.
7. Reviewed static copy sources now cover `/`, `/download`, `/providers`, `/privacy`, `/security`, `/changelog`, and `/support`, plus a Store listing draft. They are source material only and are not deployed public pages.

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
- Approve the public product name, package identity, publisher, version scheme, framework-dependent versus self-contained model, Store/direct-distribution route, and signing/provenance process. The recorded package-mode comparison recommends self-contained for a simple zero-budget GitHub tester artifact and keeps framework-dependent credible for Store distribution, pending exact-channel tests.
- Verify Microsoft Forms ownership, notifications, retention, privacy wording, and monitoring. Deploy the reviewed privacy, security, provider-status, independence, unofficial-product, session-data, purchase-boundary, support, and changelog copy on the independent website.
- Test the exact signed bytes intended for publication, record certificate chain and SHA-256, create release notes and a source tag, publish first as a prerelease, then download and repeat checksum/install/launch/uninstall validation.

## Open security findings

Standard scan `50e49bc8-acaa-4684-9639-5fbd29f91623` at `c243c16` covered 108/108 tracked-file receipts and found no backdoor, covert telemetry, sensitive page-to-native collection, arbitrary command execution, automatic download execution, hardcoded secret/private key, or unexpected package capability. It did not close two Low provider-policy findings:

1. Non-Gemini providers still lack exact observed purchase-route rules, so a same-origin checkout path may remain embedded.
2. Grok currently permits the full exact `x.com` application origin pending privacy-safe evidence of the minimum authentication route.

Both require focused account/runtime evidence and regression vectors for top-level, frame, and popup navigation. Native warnings and strict origin checks mitigate but do not close them.

## Open repository maintenance

- Dependabot PR #16 proposes the pinned `actions/checkout` v7.0.1 commit. Its original five CI jobs passed, and the pinned commit matches the official v7.0.1 release, but the PR base predates current master and GitHub currently reports mergeability as unknown. Refresh and re-run it after this release-hardening branch lands; do not merge the stale result as current evidence.

## Final go/no-go rule

Public Beta is **No-Go** while any item in `BETA_RELEASE_CHECKLIST.md` remains unchecked, unless the PRD and release scope are explicitly revised and the omitted behavior is documented as a user-visible Beta limitation. The exact published bytes—not an earlier local build—must pass the final package and supported-environment checks.
