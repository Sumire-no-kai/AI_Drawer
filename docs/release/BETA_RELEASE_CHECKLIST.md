# Public Beta release checklist

This checklist separates an internal package build from a publishable Public Beta. Every unchecked item is a release blocker unless the PRD is explicitly revised.

Current merged baseline, directly executable checks, external-device/account Gates, and the agreed first-Beta platform scope are recorded in [PRE_RELEASE_STATUS.md](PRE_RELEASE_STATUS.md). The first-Beta runtime target is Windows 10 x64, Windows 11 x64, and Windows 11 ARM64; x86 remains build/inspection coverage only.

## Product and evidence gates

- [x] Native MVP settings cover default provider, configurable shortcut, packaged startup preference, tray/exit behavior, always-on-top, and bounded window placement persistence.
- [x] Native cache/reset-all scope and download filename/destination/caution policies are implemented with automated policy and settings-UI coverage.
- [x] Native WebView security defaults, bounded recovery decisions, exact provider-home origin contracts, and off-screen window placement handling have repeatable automated coverage.
- [x] Windows 11 x64 no-account acceptance covers the real five-minute live-WebView budget, four-workspace pressure, Keep active, same-profile restoration, cache/reset APIs, Renderer/GPU/Browser recovery, tray, shortcut, exact Exit, and zero process/profile residue.
- [x] Native UI Automation passes on the current Light/150% DPI environment and under temporary Dark/200% text/reduced-motion/transparency-off settings, with changed Windows settings restored after the run.
- [ ] M2 acceptance evidence is complete or the remaining scope is explicitly accepted as a documented Beta limitation.
- [ ] M3 navigation, commerce, diagnostics, disclosure, and security-review findings are closed with focused runtime evidence.
- [ ] Every advertised provider status is backed by the compatibility Gate; unverified entries remain clearly labelled.
- [x] The provider selector bundles no provider graphic marks; any future mark requires current brand-guideline evidence and any required written authorization before entering a public artifact.
- [ ] The measured live-WebView budget and supported Windows/architecture matrix are recorded without extrapolation.

## Identity, packaging, and signing

- [ ] Final public product name, package identity, publisher, version scheme, and upgrade behavior are approved.
- [ ] Framework-dependent versus self-contained MSIX packaging is decided and verified.
- [ ] The public MSIX is signed through an approved secret-backed process; no certificate or private key is stored in the repository or workflow artifact.
- [ ] Signature, certificate chain, SHA-256 checksum, source commit, and version tag are independently verified.
- [ ] Clean install, update, rollback path, true exit, and uninstall are exercised on the supported matrix.

## Public surfaces and support

- [ ] The independent website is deployed from its own repository and release lifecycle with `/`, `/download`, `/providers`, `/privacy`, `/security`, `/changelog`, and `/support` routes.
- [ ] Privacy, security, provider-status, independence, unofficial-product, WebView2 session-data, and purchase-boundary disclosures agree across the app, website, README, and release notes.
- [x] GitHub private vulnerability reporting is enabled, linked from the repository security policy and native Feedback & Support surface, and kept separate from public issue templates.
- [x] The native shell warns that feedback must not include prompts, responses, page content, sensitive URLs, account identifiers, credentials, cookies, tokens, payment data, DOM captures, or network traces; the provider-evidence template remains separate from native-shell feedback.
- [x] The approved general feedback destination is `https://forms.cloud.microsoft/r/WLQySVad7g`; both Feedback & Support and the Home missing-provider action open this fixed HTTPS URL in the system browser without appending application, provider, account, or build data.
- [ ] Microsoft Forms ownership, response notifications, retention, privacy wording, and ongoing monitoring are verified before the same destination is used as the Microsoft Store support URL.
- [x] The native Feedback & Support surface exposes the general Microsoft Forms route, the bounded public provider-evidence route, build information, and the distinct private security-reporting route without creating a provider WebView.
- [x] The local BMC reminder policy is deterministic, becomes count-eligible at seven successful workspace opens but never during the first seven days, supports permanent dismissal, and requires both 90 days and a later major release after `Not now`.
- [x] The BMC reminder placement, disclosure, keyboard focusability, `Not now`, and `Don't ask again` behavior pass isolated UI Automation on the native Home surface without creating a provider WebView.
- [ ] The reviewed BMC HTTPS destination opens in the system browser from both Home and Settings; this external-browser launch remains intentionally unexercised in automated checks.

## Release publication

- [x] CI passes from a clean checkout with the pinned SDK on the reviewed merged `master` commit.
- [x] The internal unsigned-candidate tool verifies its package architecture, disabled startup-task declaration, reviewed capability set, unsigned state, private-key absence, and SHA-256 metadata.
- [ ] The signed candidate is tested as the exact bytes intended for publication.
- [ ] Release notes list verified changes, known limitations, supported environments, checksum, and rollback guidance.
- [ ] A version tag points to the reviewed source commit.
- [ ] The GitHub release is initially marked as a prerelease and is not promoted until post-publication download and install checks pass.
