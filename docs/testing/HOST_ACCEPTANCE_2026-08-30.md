# Windows 11 x64 Beta-hardening acceptance — 2026-08-30

This record captures development-host work that can be completed without provider accounts, sensitive provider content, another Windows version, ARM64 hardware, a production certificate, or public deployment. It does not establish provider compatibility or Public Beta readiness by itself.

## Target and host boundary

- Merged baseline: `master` `a4bc03a1d15041aa785513bc0bdc67d172a1ae94`; post-merge CI run `33064037373` passed all five maintained jobs on that exact commit.
- Release-hardening implementation commit: `e465ad06c5274d5b134671cdde12313881b79f29` on `codex/beta-release-hardening`.
- Host evidence remains Windows `10.0.26200.9168`, x64, with WebView2 Runtime `151.0.4129.107`. Cross-build results are not ARM64-device or Windows 10 runtime evidence.
- All provider-facing runtime probes used generated temporary profiles and the fixed `https://example.com/` acceptance origin. No provider account, prompt, response, DOM, credential, cookie, token, payment data, or provider network trace was read or stored.

## Build, test, dependency, and static review

- Production application x64, x86, and ARM64 Debug/Release builds passed with zero warnings and errors.
- Compatibility Lab x64 Debug/Release and application-test x64 Debug/Release builds passed with warnings as errors.
- Core passed 47/47 checks. Application tests passed 22/22 without UI and 26/26 with UI. After correcting the test's asynchronous Settings persistence observation, the full suite passed three consecutive times.
- All four maintained projects passed `dotnet format --verify-no-changes --no-restore`. All three PowerShell tools passed parser checks.
- Fresh NuGet audits reported no known vulnerable direct or transitive packages in the four maintained projects. Package references match `THIRD_PARTY_NOTICES.md`; focused secret/private-key pattern review found no committed credential material.
- Explicit ARM64 self-contained publish passed when the publish profile was selected intentionally. Removing implicit profile selection also allowed ordinary architecture/package builds to honor framework-dependent properties.

## Fixed system-browser acceptance

- Routine UI Automation records only four fixed public HTTPS destinations in an isolated Debug test root: Microsoft Forms, Buy Me a Coffee, the provider-compatibility issue form, and GitHub Private Vulnerability Reporting. It cannot record arbitrary provider or external destinations.
- One explicit `--live-external-uri` run asked Windows to open the two Forms and two BMC call sites and recorded four successful launcher returns. It did not inspect the browser, tabs, accounts, or page content.
- Release builds contain no record-only path. Normal browser handoff continues to use Windows and preserves existing query/fragment removal for provider-generated external links.

## No-account WebView2 runtime

- Fast report: `artifacts/runtime-acceptance/beta-hardening-fast-20260830.json`; 16/16 checks, eight snapshots, cold Home 438 ms; SHA-256 `96923a000bfc7f0b427a8b02d6450b4b6bb9bb5b1804cd3f9c02af793e2742ba`.
- Five-minute report: `artifacts/runtime-acceptance/beta-hardening-five-minute-20260830.json`; 20/20 checks, eight snapshots, cold Home 444 ms; SHA-256 `b5acb155d34ac706d2a03a2e195535b437f44ca2730fcf278dcef77b3d712913`.
- The full run observed 8 WebView2 processes and about 395.2 MiB at the four-workspace burst, 7 processes and about 411.8 MiB after the grace period, and 6 processes and about 402.4 MiB at stable pre-fault state. Only process-count convergence is claimed; working set did not monotonically decrease.
- Keep active survived resource pressure, an ordinary inactive workspace was released, and restoration reused the same isolated profile. Clear cache, selected-provider reset, reset-all, Renderer/GPU/Browser recovery, tray, shortcut, single instance, exact Exit, and zero residual process/profile state passed.

## Package-mode evidence

Every candidate below was unsigned, uninstalled, unpublished, and independently reopened by `tools/Test-BetaCandidate.ps1`.

| Architecture and mode | Main MSIX | Delivery size |
| --- | ---: | ---: |
| x64 framework-dependent | 29,445,987 bytes | 159,029,775 bytes across five MSIX files |
| x64 self-contained | 91,345,402 bytes | 91,345,402 bytes |
| ARM64 framework-dependent | 29,017,960 bytes | 158,601,748 bytes across five MSIX files |
| ARM64 self-contained | 87,879,391 bytes | 87,879,391 bytes |

Clean-source evidence was then regenerated at `e465ad06c5274d5b134671cdde12313881b79f29`:

- x64 self-contained: `91,345,411` bytes; SHA-256 `1b09f4fa6f14c726f1e213e673a8ad9ca8fc9b58349948451d478af754dc799a`.
- ARM64 framework-dependent: main `29,017,979` bytes; delivery `158,601,767` bytes across five MSIX files; SHA-256 `a285cf8569aa7aeab2e8744fbb416d3dc38f1c421f471cea45c3221f8107fc8a`.

The framework delivery total includes multi-architecture dependency MSIX files and must not be described as the Microsoft Store download size. Self-contained is operationally simpler for one-file GitHub tester distribution; framework-dependent remains credible for Store delivery. This evidence informs but does not make the final packaging decision. Missing `mspdbcmf.exe` still prevents symbol-package generation.

## Security review

- Codex Security diff scan `85d2444c-7211-4be9-83ec-82bc53aeb3fe` sealed the immutable range `a4bc03a1d15041aa785513bc0bdc67d172a1ae94...e465ad06c5274d5b134671cdde12313881b79f29`. Its two native C# review items closed with no reportable finding.
- Native workbench coverage was partial for the full diff because PowerShell/workflow/document files were not emitted as review items. Manual review covered all 21 changed files and found the public signer-pinning gap recorded in `docs/security/SECURITY_REVIEW.md`.
- The verifier now fixes the application identity to `AIDrawer.App` and requires a public caller to supply the expected source commit, publisher, and signer thumbprint independently. A temporary sidecar probe confirmed that missing public trust inputs fail closed; the fixture was restored and both clean internal candidates still pass reinspection after that change.
- The prior two Low provider-policy findings remain open and require privacy-safe account evidence: exact non-Gemini purchase routes and the minimum Grok/X authentication boundary.

## Still outside this host result

- Windows 10 x64 and Windows 11 ARM64 matching-device runtime matrices.
- Provider login, authentication popups, session persistence, conversation, history, uploads/downloads, permissions, reset behavior, purchase-route observation, and recovery for every advertised provider.
- Live High Contrast, Narrator, 200% display DPI, and human visual/contrast/clipping review.
- Approved public name, package identity, publisher, versioning, dependency mode, certificate/provenance, signed install/update/startup/rollback/uninstall, exact release bytes, tag, notes, prerelease download, and post-download installation.
- Microsoft Forms ownership/notifications/retention/monitoring, independent website deployment, final public support/privacy URLs, and Store submission.
