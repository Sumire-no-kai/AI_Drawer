# Security review record

## Automated checks in this repository

- Core policy tests cover restrictive restore locators, workspace lifecycle, shortcut validation, settings normalization, download names and collision handling, window placement clamping, WebView security defaults, and bounded recovery decisions.
- Application policy tests cover encrypted locator persistence, corrupt/newer/locked session recovery, settings persistence, provider-catalog contracts, strict navigation, and known Gemini purchase-route blocking.
- Internal-candidate validation inspects the produced MSIX for unsigned state, expected architecture, exact startup-task declaration, reviewed capabilities, checksum metadata, and absence of private-key files.
- CI performs x64/x86/ARM64 production builds with warnings as errors, x64 Compatibility Lab and policy checks, and formatting verification.

## Required before public release

- Complete the provider-specific runtime Gates without claiming provider compatibility early.
- Run an independent repository-level security scan and resolve or record every finding.
- Validate a signed candidate on the supported Windows/architecture matrix, including installation, update, rollback, and uninstall.
- Enable and test private vulnerability reporting; publish the final contact only after that channel exists.

This record describes code and local validation coverage, not a claim that external provider or Store review has passed.
