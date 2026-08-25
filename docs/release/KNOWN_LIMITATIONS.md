# Known limitations for internal M4 candidates

This file describes the current repository state. It is not a Public Beta release note and does not establish provider or platform compatibility.

- The x64, x86, and ARM64 automated candidates are intentionally unsigned and are for build-pipeline and package inspection only. They must not be published as end-user downloads.
- Package identity, public versioning, publisher identity, certificate ownership, and the GitHub Releases signing design are not yet approved.
- The current package remains framework-dependent. The PRD decision between framework-dependent and self-contained public MSIX packaging is still open.
- GitHub private vulnerability reporting is enabled and documented. The native Feedback & Support surface links to it separately from the privacy-safe public bug and provider-evidence templates; no ordinary support email has been approved yet.
- M2 runtime acceptance remains incomplete, including the measured live-WebView budget and portions of recovery, reset, accessibility, architecture, and Windows-version coverage.
- Provider login, external navigation, popup, purchase blocking, upload/download, media, and recovery behavior still require provider- and environment-specific evidence. Current status labels are not universal support claims.
- The packaged startup-task declaration passes unsigned x64 package validation, but enabling it from an installed package, disabled-by-user/policy states, hidden startup, and post-sign-in behavior still require packaged runtime checks. Unpackaged UI automation cannot establish those results.
- Cache clear, provider website-data reset, reset-all partial failure, and the download destination/caution flow are implemented but have not been exercised against signed-in real-provider profiles. These checks remain deliberately deferred rather than reported as passed.
- x86 and ARM64 device runtime, Windows 10 22H2, Windows 10 Enterprise LTSC 2019, and a supported Windows 11 release have not all passed the release matrix. Cross-architecture compilation is not device evidence.
- The independent website, final product name/domain, monitored website support destination, public privacy/security/provider-status pages, signing, installation, upgrade, rollback, and uninstall acceptance remain open.
