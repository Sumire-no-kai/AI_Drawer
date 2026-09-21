# ADR 0003: Retained conversation tabs and optional split view

Status: Accepted, 2026-09-21. Supersedes the automatic live-view budget and Keep active policy in ADR 0001.

## Problem and decision

Users need several independent conversations with the same or different providers, with reliable switching and occasional comparison. Automatically discarding a background page after five minutes defeats this workflow: restoring a conversation address cannot restore an unsent draft or an ongoing generation.

Create pages lazily and retain them across tab switches and window hiding. Remove the two-view steady budget, three-view hard cap, automatic disposal timer, and Keep active UI. Memory consumption grows with opened pages; this is an explicit trade-off, not a measured capacity claim. Provider behavior and operating-system process termination remain outside the application's control.

Offer explicit Release page with a loss-of-state confirmation. Native-known navigation, permission, download, and controlled-popup operations prevent release. A released page stays released through layout changes until the user selects it or explicitly reloads it. Out-of-memory recovery must not discard unrelated pages.

## Ownership and interaction

The session controller owns ordered tab identities, names, selection, pane layout, and persistence. The runtime coordinator owns live WebViews and their native events. Focused tab, visible panes, and retained pages are separate concepts.

Tabs support rename, same-provider creation, ordering, search, Ctrl+Tab cycling, and Ctrl+1–9 positional selection. Ctrl+T opens another tab with the current provider; Ctrl+N and the plus button open the provider chooser. Same-provider tabs share a provider profile, not isolated accounts.

Split view places two existing tabs side by side without cloning pages. Selecting a third tab replaces the focused pane. Selecting an already visible tab focuses it. Leaving split view retains the focused tab and backgrounds its partner. Below the minimum split width, show only the focused page while retaining the pair. Selecting Home or an unavailable provider ends the split.

## Persistence and privacy

Schema 2 stores tab order, user-entered names, focused tab, pane IDs, and divider ratio. Before the first migration write, copy the readable schema-1 file to a timestamped recovery backup. Keep the existing session filename so older builds reject the newer schema instead of silently overwriting it. Downgrade requires explicit recovery using the preserved backup; automatic downgrade is not supported.

Names are local plaintext metadata and can contain information the user chooses to enter. Backups retain the old metadata and protected locators. Conversation locators remain current-user encrypted, provider-allowlisted, and free of query strings and fragments. No prompts, responses, DOM, credentials, cookies, or tokens are read into application persistence.

External reference handoff uses a separate transient policy. Only native user-initiated links from a reviewed provider app origin may retain reviewed query keys and simple anchors, after confirmation. Sensitive parameter names cause query and fragment removal; unknown parameters are omitted. Authentication and known purchase restrictions remain intact. This is deliberately not a promise of lossless forwarding for arbitrary URLs.

## Evidence boundary

This change implements the design and updates regression coverage source. Local test execution and GUI/provider acceptance are deferred at the owner's request. Earlier automatic-disposal runtime evidence does not validate this lifecycle. Compile results and automated PR checks must be reported separately from device/provider acceptance.
