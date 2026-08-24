# ADR 0002: Package-aware local data and uninstall semantics

## Status

Proposed — requires the public distribution-model decision before implementation.

## Context

AI Drawer stores only native settings, workspace metadata, current-user-encrypted restricted restore locators, and WebView2 provider-profile data. The current development path uses `%LOCALAPPDATA%\\AI Drawer`; tests may override that root through `AI_DRAWER_TEST_DATA_ROOT`. The primary public distribution target is MSIX and Microsoft Store, but the package identity, upgrade ownership, framework-dependency model, and any parallel unpackaged distribution are not yet approved.

Changing the release storage root without a migration plan can lose workspace metadata or leave provider session data behind unexpectedly. Conversely, deleting WebView2 profile data during an update or an ordinary repair would silently sign users out.

## Decision required before implementation

The release owner must choose one supported public model:

1. **MSIX/Store only (recommended):** use the packaged app-local storage contract and document that uninstall removes native AI Drawer data and its WebView2 profiles unless Windows preserves them for a package-management reason.
2. **MSIX plus unpackaged installer:** specify two roots, whether either direction migrates native settings, and explicitly never migrate browser cookies or provider website data automatically.

For either choice, implementation must:

- detect packaged versus unpackaged execution without inferring it from a path;
- migrate only validated native settings and workspace metadata, never cookies, tokens, credentials, prompts, responses, DOM, or payment data;
- keep the migration idempotent, backed up, and recoverable;
- disclose reset and uninstall scope separately from provider-account deletion;
- test clean install, update, rollback, uninstall, and reinstall on the exact signed package.

## Consequences

No storage-root migration is implemented in the pre-alpha shell. Existing local development data remains untouched until the public model and upgrade semantics are approved. This ADR is a release blocker, not an assertion about Microsoft Store uninstall behavior.
