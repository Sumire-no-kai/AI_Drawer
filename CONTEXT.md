# AI Drawer domain context

## Ubiquitous language

- **Provider profile** — the WebView2 browser profile shared by every AI Drawer workspace for one provider. It owns that provider's cookies, authentication, cache, permissions, and site storage. The MVP intentionally supports one provider profile per provider.
- **Conversation workspace** — a native AI Drawer tab with stable identity, ordering, provider selection, Keep active preference, and an optional restore locator. It continues to exist when no WebView is live.
- **Live WebView** — the disposable embedded browser process and visual surface currently realizing a conversation workspace. It is a bounded runtime resource, not the workspace's identity.
- **Restore locator** — the latest provider URL that passed a provider-specific allowlist and was reduced to an HTTPS origin plus opaque conversation path. It is encrypted at rest and never contains query parameters, fragments, page content, credentials, cookies, or tokens.
- **Transient navigation target** — the latest app-domain HTTPS path considered safe for same-process WebView recreation. It excludes query parameters, fragments, authentication origins, user-info, and custom ports, and is never serialized.
- **Operation protection** — a native-known navigation, permission request, or download that prevents automatic WebView disposal until the operation completes or the view is explicitly closed.
- **Grace period** — a short interval after a workspace becomes inactive during which its live WebView is normally retained to make rapid switching cheap.
- **Keep active** — a user preference that makes an inactive workspace the last candidate for runtime disposal. It does not promise that the operating system will keep a process alive.
- **Disposed workspace** — a conversation workspace whose live WebView has been released. Its native tab remains and can recreate the WebView from a valid restore locator or the provider home page.

## Invariants

- A conversation workspace must not disappear merely because its live WebView is released.
- Provider profile data is never copied into workspace persistence.
- A restore locator is revalidated both before it is stored and before it is used.
- A transient navigation target must never cross the persistence seam.
- A workspace with a native-known protected operation must not be selected for automatic disposal.
- Resetting website data applies to the provider profile and clears restore locators for all workspaces using that provider.
- Provider compatibility remains unverified until its PRD Gate passes.
