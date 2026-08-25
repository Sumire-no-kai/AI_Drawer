# Versioning and rollback policy

## Status

Draft — final package identity, publisher, signing owner, and public version scheme are not approved.

## Proposed rules

- Use four numeric MSIX version components and monotonically increase every Store submission.
- Tag the reviewed source commit before a candidate is signed.
- Publish release notes, SHA-256, known limitations, supported matrix, and rollback instructions with every prerelease.
- Roll back by publishing a newer signed corrective package; do not reuse or decrease a Microsoft Store package version.
- Treat the signed package bytes as the release subject. Rebuilding the same source after signing is not the same verified artifact.

## Storage and rollback

An update or rollback must not delete provider browser data or native workspace metadata. Any future migration must follow ADR 0002 and be tested on the exact install/update/rollback route.

## Required release evidence

1. Clean install and first run.
2. Upgrade from the immediately previous supported build.
3. True exit and restart.
4. Provider-data reset scope disclosure.
5. Uninstall and reinstall behavior, documented separately from provider-account deletion.
