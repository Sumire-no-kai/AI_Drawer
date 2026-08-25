# Security review record

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
