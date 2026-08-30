# Microsoft Store listing draft

> Draft only. Final name, identity, publisher, version, screenshots, supported matrix, privacy URL, support URL, and signed package remain release-owner Gates.

## Short description

A lightweight, privacy-respecting Windows workspace for switching among official AI web applications.

## Description

AI Drawer gives you one keyboard-first native Windows home for official AI web applications you already use. Create several native workspaces, switch providers quickly, hide to the tray, and return with a configurable global shortcut.

Provider websites continue to own sign-in, conversations, history, models, uploads, downloads, subscriptions, billing, and account settings. AI Drawer has no AI backend, API keys, AI Drawer account, additional AI subscription, or analytics service.

AI Drawer does not read or store prompts, responses, page DOM, credentials, cookies, tokens, payment data, or provider conversation content. WebView2 keeps provider site data inside an application-specific local profile so provider sign-in can persist separately from the user's normal browser profile.

AI Drawer is independent and unofficial. It is not affiliated with, endorsed by, sponsored by, or supported by listed AI providers. Provider compatibility varies and is shown with evidence-bounded status labels.

Purchases are not processed inside AI Drawer. Known provider purchase routes are blocked on a best-effort basis; use Edge, Chrome, or another trusted browser to purchase or manage subscriptions.

## Required links before submission

- Privacy policy: final independent `/privacy` URL.
- Support: final monitored independent `/support` URL.
- Security reports: GitHub Private Vulnerability Reporting.
- Website: final independent product root.
- License/source: public repository and Apache License 2.0 notices.

## Initial known-limitations summary

Provider behavior depends on third-party websites and may change without an AI Drawer update. Only providers and platform combinations that passed the release matrix may be described as supported. The first Beta must state the exact Windows builds and x64/ARM64 device evidence rather than claiming every Windows 10/11 edition.
