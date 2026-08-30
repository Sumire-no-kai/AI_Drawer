# Security

AI Drawer treats every embedded provider page as untrusted web content relative to native Windows privileges. Public builds disable host objects, page-to-native web messages, general autofill, password autosave, and developer tools. The application does not inject scripts into provider pages or expose filesystem, terminal, or privileged native commands.

Navigation uses parsed HTTPS origins, exact reviewed host boundaries, default ports, and no user-info. Certificate errors fail closed. Unreviewed top-level links require native confirmation before a query- and fragment-stripped system-browser handoff; unsupported schemes and external frame navigation are denied.

Known purchase routes are blocked on a best-effort basis, but AI Drawer cannot inspect or rewrite provider page content and cannot guarantee that an unknown same-origin purchase route has already been identified. Users should make purchases in a trusted normal browser.

## Report a vulnerability

Use [GitHub Private Vulnerability Reporting](https://github.com/Sumire-no-kai/AI_Drawer/security/advisories/new). Do not publish exploitable details in a public issue or ordinary feedback form.

Remove prompts, responses, page content, full sensitive URLs, account identifiers, credentials, cookies, tokens, payment data, DOM captures, and provider network traces. Fictional values and sanitized HTTPS origins are preferred.
