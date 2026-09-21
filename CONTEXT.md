# AI Drawer domain context

## Ubiquitous language

- **Provider profile** — the WebView2 browser profile shared by every AI Drawer workspace for one provider. It owns that provider's cookies, authentication, cache, permissions, and site storage. The MVP intentionally supports one provider profile per provider.
- **Conversation workspace** — a native AI Drawer tab with stable identity, ordering, provider selection, user-entered name, and an optional restore locator. It continues to exist when no WebView is live.
- **Live WebView** — the disposable embedded browser process and visual surface currently realizing a conversation workspace. It remains alive across switching; the user can release it without deleting the tab.
- **Restore locator** — the latest provider URL that passed a provider-specific allowlist and was reduced to an HTTPS origin plus opaque conversation path. It is encrypted at rest and never contains query parameters, fragments, page content, credentials, cookies, or tokens.
- **Recovery-blocked session** — a locally stored workspace session that could not be safely interpreted in full (for example, because it is corrupt, unavailable, too large, from an unsupported schema, or contains an unreadable protected locator). It remains the authoritative prior record until the user explicitly retries or creates a backup before continuing.
- **Transient navigation target** — the latest app-domain HTTPS path considered safe for same-process WebView recreation. It excludes query parameters, fragments, authentication origins, user-info, and custom ports, and is never serialized.
- **Controlled provider popup** — a non-persisted native window created only for a provider- or authentication-domain popup request. It shares the originating provider profile, is closed with its parent workspace, and is not a conversation workspace.
- **Operation protection** — a native-known navigation, permission request, or download that prevents explicit page release until the operation completes or the view is explicitly closed.
- **Disposed workspace** — a conversation workspace whose live WebView has been released. Its native tab remains and can recreate the WebView from a valid restore locator or the provider home page.

- **Focused tab** — the workspace targeted by toolbar actions and keyboard selection.
- **Pane layout** — one or two tab IDs plus the focused ID and divider ratio; independent of retained WebViews.

## Invariants

- Switching, hiding, and resizing must not implicitly release pages or resume explicitly released pages.
- A conversation workspace must not disappear merely because its live WebView is released.
- Provider profile data is never copied into workspace persistence.
- A recovery-blocked session must never be overwritten by an empty or partial replacement without an explicit backup-and-continue decision.
- A restore locator is revalidated both before it is stored and before it is used.
- A transient navigation target must never cross the persistence seam.
- A workspace with a native-known protected operation must not be selected for explicit page release.
- A controlled provider popup keeps its originating workspace protected from explicit page release while the popup is open.
- Resetting website data applies to the provider profile and clears restore locators for all workspaces using that provider.
- Provider compatibility remains unverified until its PRD Gate passes.
