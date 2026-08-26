# Security review record

## 2026-08-26 repository Standard scan

Codex Security Standard scan `8745c8a7-9be7-42d9-858d-5f77e4a51055` completed against immutable master revision `0c7fd79593925bec364b67779102826bac86b0eb`. It reviewed all 106 tracked files and closed six planned security surfaces. Delegated baseline and focused-worker slots were unavailable, so this was a sequential source audit rather than an independent multi-reviewer result.

The scan found no backdoor, covert telemetry, page-to-native DOM/prompt/response/credential/cookie/token collection, arbitrary command execution, automatic download execution, hardcoded secret, or unexpected package capability. Two validated **Low** findings remain open:

1. `csf_37bdf242ce68bbe0741b0742`: every non-Gemini provider still has an empty reviewed purchase-route policy. A checkout path hosted on an allowed provider application origin therefore remains embedded until its exact privacy-safe origin/path rule is observed and added. Historical evidence already records reachable ChatGPT and Grok purchase pages, but intentionally did not retain the exact routes.
2. `csf_1f67227fdc4368f80870ea93`: Grok currently treats the entire exact `x.com` origin as provider application content. Removing it without first observing the route required by X/Grok authentication could break login, so the production rule must be narrowed from authorized privacy-safe route evidence rather than guessed.

Both findings are mitigated by strict HTTPS/default-port/user-info checks, exact host comparison, certificate fail-closed behavior, native permission/download confirmation, disabled page-to-native bridges and autofill, the native payment warning, and the absence of native payment-data access. They are not closed by those mitigations. Public support for the affected provider routes still requires focused account/runtime evidence, narrow policy updates, and top-level/frame/popup regression vectors.

The scan target predates the no-account acceptance and Microsoft Forms branch. That branch is reviewed separately as a source diff and keeps its provider-origin and profile-action controls inside `#if DEBUG`, restricted to a generated runtime-acceptance directory under the Windows temporary root.

## 2026-08-25 independent repository scan and remediation

An independent repository-level scan completed against `d364d126e02f2a1ea19881e942e312de2955fa68`. It confirmed four source findings at that revision:

1. **Medium:** the internal Compatibility Lab left unreviewed navigation, popups, permission requests, and downloads to WebView2 defaults and did not keep a visible sanitized origin indicator.
2. **Low:** the Lab preferred a persistent profile on an unverified `D:` volume when present.
3. **Low:** the download filename sanitizer preserved bidirectional formatting controls that could visually disguise a file extension.
4. **Low:** GitHub Actions referenced mutable major-version tags rather than immutable action commits.

The current remediation branch makes the Lab fail closed by default, selects a local disposable profile, and exposes an explicit fresh-profile-only observation mode for scoped provider evidence. Known purchase routes and certificate errors remain blocked in both modes, and diagnostics remain origin-only and in memory. Filename sanitization now replaces control and bidirectional-formatting characters. Workflow actions are pinned to reviewed full commit SHAs, Dependabot monitors GitHub Actions updates, and candidate inspection rejects private-key extensions both inside the MSIX and in its output tree.

Focused policy and UI Automation cover the new Lab defaults, high-contrast and focus-cycle resources, memory-pressure victim selection, bounded failure recovery, support-reminder persistence, and bidirectional filename cases. Production x64/x86/ARM64 Debug and Release builds and x64 Lab/test Debug and Release builds passed with warnings treated as errors. An online NuGet audit reported no known vulnerable direct or transitive packages for the production app, Lab, or application-test project. The generated x64 internal candidate remained unsigned and uninstalled; its package contract passed and its SHA-256 was `f6b40b467a646336b0a4c31515b1128062e9de2696491cb291335d9745b02114`.

This closes the four source findings on the remediation branch. It does not close provider-specific navigation, authentication, popup, permission, download, commerce, or fault-injection runtime Gates, and it does not establish signing, Store, installation, upgrade, rollback, or uninstall acceptance.

## Automated checks in this repository

- Core policy tests cover restrictive restore locators, workspace lifecycle, shortcut validation, settings normalization, download names and collision handling, window placement clamping, WebView security defaults, and bounded recovery decisions.
- Application policy tests cover encrypted locator persistence, corrupt/newer/locked session recovery, settings persistence, provider-catalog contracts, strict navigation, and known Gemini purchase-route blocking.
- Internal-candidate validation inspects the produced MSIX for unsigned state, expected architecture, exact startup-task declaration, reviewed capabilities, checksum metadata, and absence of private-key files.
- CI performs x64/x86/ARM64 production builds with warnings as errors, x64 Compatibility Lab and policy checks, and formatting verification.

## Required before public release

- Complete the provider-specific runtime Gates without claiming provider compatibility early.
- Re-run focused security review after any later security-boundary or release-pipeline change and resolve or record every finding.
- Validate a signed candidate on the supported Windows/architecture matrix, including installation, update, rollback, and uninstall.
- Enable and test private vulnerability reporting; publish the final contact only after that channel exists.

This record describes code and local validation coverage, not a claim that external provider or Store review has passed.
