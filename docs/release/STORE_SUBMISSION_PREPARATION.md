# Microsoft Store submission preparation

For an MSIX submission, Partner Center lets publishers enter a product website and a support contact URL or email. Microsoft describes both as optional but recommended for non-Xbox submissions; the support field is required for Xbox. See [Microsoft's MSIX support-information guidance](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/support-info).

AI Drawer should provide all of the following before its first Store submission:

1. A stable support URL on the independent product website, with a monitored support email as fallback.
2. A public privacy-policy URL. Partner Center requires one when the product accesses, collects, or transmits personal information, and it can require one when declared capabilities indicate that possibility. AI Drawer will supply one before release because it is a full-trust desktop shell with provider-session data, while the release owner completes the final Partner Center declaration. See [Microsoft's MSIX support-information guidance](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/support-info) and the [Microsoft Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies-and-code-of-conduct).
3. A distinct private security-reporting channel; do not direct sensitive reports to public issues or an ordinary support mailbox.
4. A final name, Store description, screenshots, Store logo, package identity, publisher, version, and signed package.

## In-app information architecture recommendation

Add **Feedback & Support** as a top-level native destination beside **Settings** and **About & Privacy**, rather than burying it in About. It should show a normal support route, a privacy-safe bug-report route, version/build information, and a separate security-reporting link once the private channel is live. It must not encourage users to include prompts, responses, page content, credentials, cookies, tokens, payment data, or full sensitive URLs.

The pre-alpha app now uses the official repository's privacy-safe public issue templates for bugs and provider evidence, and GitHub Private Vulnerability Reporting for security issues. No placeholder email address is embedded. The final website support URL or monitored support email still requires owner approval and operational monitoring before Store submission.
