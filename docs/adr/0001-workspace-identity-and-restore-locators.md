# ADR 0001: Separate workspace identity from WebView lifetime

## Status

Accepted — 2026-08-21

## Context

AI Drawer must support several native conversation workspaces without keeping an unbounded number of WebView processes alive. Releasing the third workspace's WebView previously lost its exact conversation and reopened the provider home page. The product privacy boundary forbids reading or storing page DOM, prompts, responses, credentials, cookies, tokens, or payment data.

## Decision

Each native conversation workspace has stable persisted identity independent of its live WebView. Inactive WebViews enter a low-memory grace period and may later be disposed, while the native workspace remains identifiable and reloadable.

For providers with a reviewed URL shape, AI Drawer may persist one restricted restore locator: an HTTPS provider origin plus an allowlisted opaque conversation path. Query parameters and fragments are removed, the value is encrypted for the current Windows user, and it is revalidated before restore. Providers without a reviewed rule fall back to their home page.

The MVP uses one WebView2 provider profile per provider. Multiple accounts for the same provider are deferred because introducing account-scoped profiles changes profile reset, navigation, migration, and privacy semantics.

## Considered options

- Keep every WebView alive: best switching fidelity but unbounded memory and process pressure.
- Dispose inactive WebViews without a locator: bounded memory but silently loses exact workspace location.
- Persist full browser state or page content: closer to browser session restore but violates product scope and privacy boundaries.
- Restricted restore locator plus provider profile: preserves useful identity with bounded data, while accepting provider-specific URL limitations.

## Consequences

- Exact restore is intentionally provider-specific and must fail closed.
- A provider URL redesign can make a stored locator unusable; the safe fallback is provider home.
- Keep active is a disposal preference, not an absolute resource guarantee.
- Pressure limits need later measurement on Windows 10 and Windows 11; this decision defines bounded behavior but does not claim measured capacity.
