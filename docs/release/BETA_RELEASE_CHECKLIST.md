# Public Beta release checklist

This checklist separates an internal package build from a publishable Public Beta. Every unchecked item is a release blocker unless the PRD is explicitly revised.

## Product and evidence gates

- [ ] M2 acceptance evidence is complete or the remaining scope is explicitly accepted as a documented Beta limitation.
- [ ] M3 navigation, commerce, diagnostics, disclosure, and security-review findings are closed with focused runtime evidence.
- [ ] Every advertised provider status is backed by the compatibility Gate; unverified entries remain clearly labelled.
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
- [ ] GitHub private vulnerability reporting or another reviewed private security channel is enabled and tested.
- [x] Bug and provider-evidence templates reject sensitive provider/account data and distinguish provider behavior from native-shell defects.
- [x] The local BMC reminder policy is deterministic, never eligible in the first seven days, supports permanent dismissal, and requires both 90 days and a later major release after `Not now`.
- [ ] The BMC system-browser link and reminder placement/disclosure pass focused runtime and accessibility checks without appearing over provider content.

## Release publication

- [ ] CI passes from a clean checkout with the pinned SDK.
- [ ] The signed candidate is tested as the exact bytes intended for publication.
- [ ] Release notes list verified changes, known limitations, supported environments, checksum, and rollback guidance.
- [ ] A version tag points to the reviewed source commit.
- [ ] The GitHub release is initially marked as a prerelease and is not promoted until post-publication download and install checks pass.
