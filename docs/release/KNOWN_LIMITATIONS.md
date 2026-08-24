# Known limitations for internal M4 candidates

This file describes the current repository state. It is not a Public Beta release note and does not establish provider or platform compatibility.

- The automated candidate is intentionally unsigned and is for build-pipeline and package inspection only. It must not be published as an end-user download.
- Package identity, public versioning, publisher identity, certificate ownership, and the GitHub Releases signing design are not yet approved.
- The current package remains framework-dependent. The PRD decision between framework-dependent and self-contained public MSIX packaging is still open.
- GitHub private vulnerability reporting is not enabled. Public Beta is blocked until a private security-reporting path exists and is documented.
- M2 runtime acceptance remains incomplete, including the measured live-WebView budget and portions of recovery, reset, accessibility, architecture, and Windows-version coverage.
- Provider login, external navigation, popup, purchase blocking, upload/download, media, and recovery behavior still require provider- and environment-specific evidence. Current status labels are not universal support claims.
- x86 and ARM64 device runtime, Windows 10 22H2, Windows 10 Enterprise LTSC 2019, and a supported Windows 11 release have not all passed the release matrix. Cross-architecture compilation is not device evidence.
- The independent website, final product name/domain, public privacy/security/provider-status pages, signing, installation, upgrade, rollback, and uninstall acceptance remain open.
