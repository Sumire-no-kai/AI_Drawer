# Windows 11 x64 host acceptance — 2026-08-27

This record captures the checks that were completed without provider accounts or sensitive provider content. It is evidence for one Windows 11 x64 development host, not a claim about Windows 10, ARM64 hardware, signed installation, or provider compatibility.

## Target and host

- Source baseline: `c243c166adee0bb98588a0c875e2ccf52e964c2e` on `codex/beta-status-refresh`, whose only baseline difference from merged `master` was release-status documentation. The later working-tree change only stabilizes UI Automation scrolling and assertions; it does not change production application code.
- Merged production baseline: `master` `0191ee5e16bede4f0da9dbefd0b9d3a6713034c9`, PR #18 head `cd925b2b2d49bb0c3cc7595f51dabe75bd9d75a6`.
- Host: Windows `10.0.26200.9168`, x64, 2560×1440 at 150% (144 DPI).
- WebView2 Runtime observed on the host: `151.0.4129.107`.
- SDK: repository-local, checksum-verified .NET SDK `10.0.400`.

## Build, static, dependency, and package checks

- Production application Debug and Release builds passed for x64, x86, and ARM64 with zero warnings and errors.
- Compatibility Lab x64 Debug and Release and application-test x64 Debug and Release builds passed.
- Core policy tests passed 47 checks. The application harness passed 21 non-GUI checks and, after the UI Automation scrolling correction, 25 complete checks.
- All four maintained projects passed `dotnet format --verify-no-changes --no-restore`.
- All three PowerShell tools passed parser checks under PowerShell 7.
- Fresh NuGet vulnerability audits reported no known vulnerable direct or transitive packages in the production app, Compatibility Lab, Core tests, or application tests. `THIRD_PARTY_NOTICES.md` agrees with the four current package references and their reviewed license terms.
- Unsigned x64 and ARM64 internal candidates passed manifest, architecture, capability, disabled-startup-task, signature-absence, private-key-absence, and metadata inspection. Their SHA-256 values are `ea4504ac7d6faecaa6fdd623f4f2ad6d601bbb83d2e499c9f42e29c192ad6fd6` and `476f7abb4e59033142922624353e5abcff0e8b193a8aaa905dce391bb3f2e197`. Missing `mspdbcmf.exe` prevented symbol-package generation. The candidates were not installed, signed, or published.

The first restore attempt was blocked by the command sandbox's network policy. The repeated scoped-network restore succeeded; this was an execution-environment restriction, not a project failure. A Release application-test DLL cannot be run unpackaged because the referenced Release app expects package identity; Debug is the intentional isolated UI/runtime test configuration. Release production compilation and unsigned package inspection passed separately.

## No-account runtime acceptance

The harness used a generated temporary data root and only `https://example.com/`. It did not use provider accounts, authentication, prompts, responses, DOM, credentials, cookies, tokens, payment data, or provider network traces.

- Fast run: 16/16 checks and 8 snapshots passed. Report SHA-256: `3683c84b2a3a81bb48c22e9c49841fefaa0cd51dc23c55ee43030278bf5b95e8`.
- Full five-minute run: 20/20 checks and 8 snapshots passed. Report SHA-256: `44238cc323257e57ee56e7167682ed88f41698281e4594cd38406d86791f5960`.
- Cold start reached the native Home surface in 1081 ms.
- The four-workspace burst measured 8 WebView2 processes and about 409.9 MiB WebView2 working set. After the real five-minute grace period, the count fell to 7, then settled at 6 before fault injection. These are measurements on this host, not a comparison with a browser or a general memory guarantee.
- Keep active survived disposal; a second ordinary inactive workspace was released; a disposed workspace recreated on the same isolated profile.
- Clear cache, selected-provider reset, and reset-all completed through real WebView2 profile APIs against the isolated Debug profiles.
- Injected Renderer, GPU helper, and Browser-process exits were contained; the required child process/environment was recreated and navigation recovered.
- Close-to-tray, global-shortcut restoration, second-launch restoration, exact Exit, process-tree cleanup, and temporary-profile release passed. No isolated process or test profile remained after exit.

## Native UI and accessibility evidence

- The complete 25-check UI Automation suite passed on the current Light environment. It covers onboarding input, BMC eligibility and both dismissal choices, Feedback & Support, Settings, minimum-window reflow, centered provider choices, keyboard-focusable actions, recovery UI, session restoration, and UI Automation names/bounds.
- A test defect was found and corrected: the harness previously invoked BMC and feedback buttons that could still be outside the ScrollViewer viewport at 150% DPI or large text. The harness now scrolls each action into view, confirms nonzero visible bounds, then invokes it; persistence and visual dismissal are checked separately. Production handlers did not require a change.
- The same suite completed with exit code 0 after temporarily selecting Dark application theme, 200% text scale, disabled client-area animation, and disabled transparency. Each prior registry value and animation state was saved and restored in `finally`; post-run probes confirmed the original absent-default theme/text keys, High Contrast off, and client-area animation on.
- Source and automated policy checks cover Light, Dark, and High Contrast resource dictionaries. Live High Contrast visual review, real Narrator speech/order, 200% display DPI, and a human contrast/clipping review remain open because those results cannot be established by resource presence or UI Automation alone.

## Security scan

Codex Security Standard scan `50e49bc8-acaa-4684-9639-5fbd29f91623` completed against immutable source revision `c243c166adee0bb98588a0c875e2ccf52e964c2e`. It inventoried and closed 108/108 tracked-file review receipts with an independent baseline review, an architecture/threat-model review, and parent validation.

No backdoor, covert telemetry, prompt/response/DOM/credential/cookie/token/payment collection, arbitrary command execution, automatic download execution, hardcoded secret/private key, or unexpected package capability was found. Two high-confidence **Low** findings remain open:

1. `csf_0db8d656fda6418a1510d9b6`: non-Gemini providers have no evidence-backed same-origin purchase rules, so an unknown same-origin checkout path can still be embedded.
2. `csf_55bb3e9afc0420210d58a011`: Grok treats the entire exact `x.com` origin as application content instead of a narrower observed route or external destination.

The scan used three available worker slots rather than the six-slot advisory target; the parent completed the remaining coverage. Its immutable target warning records the later test-only working-tree edit. Provider-account evidence must resolve the two findings without guessing paths or collecting sensitive content.

## Still outside this host result

- Windows 10 x64 and Windows 11 ARM64 matching-device runtime matrices.
- Provider login, normal use, authentication popup, upload/download, permission, cache/reset, purchase-route discovery, and recovery evidence for all advertised providers.
- Clean signed-package install, update, startup registration, rollback, uninstall, retained-data behavior, Store identity, publisher, and provenance.
- Live High Contrast, Narrator, 200% display DPI, final human visual review, and exact signed bytes on every supported environment.
- Owner checks for Microsoft Forms administration/monitoring and the final public support/privacy/website surfaces.
