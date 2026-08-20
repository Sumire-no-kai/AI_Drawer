# Provider WebView2 Compatibility Test Plan

## Purpose

This plan governs Milestone 0 testing for the initial AI Drawer provider matrix. It uses the small WinUI 3 + WebView2 Compatibility Lab to determine whether each provider is technically viable in an embedded browser without crossing the project's privacy or authentication boundaries.

Passing a page-load test does not establish support. A provider remains `Experimental` until its recorded evidence is complete and repeatable.

## Candidate matrix

| ID | Provider | Initial official web entry | Current status |
| --- | --- | --- | --- |
| `chatgpt` | ChatGPT | `https://chatgpt.com/` | Not tested |
| `claude` | Claude | `https://claude.ai/` | Not tested |
| `gemini` | Gemini | `https://gemini.google.com/` | Experimental |
| `grok` | Grok | `https://grok.com/` | Not tested |
| `deepseek` | DeepSeek | `https://chat.deepseek.com/` | Not tested |
| `doubao` | Doubao / 豆包 | `https://www.doubao.com/` | Not tested |
| `qwen` | Qwen / 通义千问 | `https://chat.qwen.ai/` | Not tested |
| `glm` | GLM / 智谱清言 | `https://chatglm.cn/` | Not tested |

The entry URLs are test starting points, not an allowlist or a provider-support claim. Login, payment, popup, and redirect domains must be observed manually and added only after review.

## Privacy and safety boundary

- Enter account details manually. Do not record accounts, credentials, cookies, tokens, prompts, responses, uploaded file contents, DOM content, request bodies, or full URLs.
- The harness may display only sanitized origins, event categories, permission kinds, process-failure categories, and local test-profile paths.
- Do not spoof a User-Agent, import browser cookies, intercept tokens, call private APIs, inject JavaScript, or automate sign-in.
- Use a dedicated non-sensitive test conversation and non-sensitive sample files.
- Stop before any purchase. A blocked route is evidence only for that route and release; it is never a blanket payment guarantee.

## Test run setup

1. Record the date, tester-assigned run ID, Git commit, Windows version/build, architecture, .NET SDK, Windows App SDK, WebView2 Runtime, country-level region, profile mode, and non-identifying account class if relevant.
2. Select one provider and either `Fresh disposable` or `Persistent` profile mode.
3. Start the WebView and interact with the provider manually.
4. Record results in [Provider Compatibility Matrix](Provider_Compatibility_Matrix.md) using only the permitted evidence.
5. End the test before choosing another provider. The harness closes the current WebView before allowing a provider or profile change.

## Gate A — authentication feasibility

Run with a fresh profile first. If login is blocked or requires a prohibited workaround, stop and record `Limited` or `ExternalOnly`; do not attempt a bypass.

| ID | Case | Pass condition |
| --- | --- | --- |
| `A01` | Fresh page load | Entry origin loads without a harness crash. |
| `A02` | Start sign-in | The provider's normal sign-in flow begins. |
| `A03` | Complete sign-in | The account reaches the provider without a prohibited workaround. |
| `A04` | Popup and redirect | Required login popup or redirect returns to a usable state. |
| `A05` | Restart WebView | A signed-in session survives a same-profile renderer restart where the provider supports it. |
| `A06` | Restart application | A signed-in session survives a full app restart where the provider supports it. |

## Gate B — core web experience

| ID | Case | Pass condition |
| --- | --- | --- |
| `B01` | New text conversation | A manually entered prompt submits and a response becomes visible without reload. |
| `B02` | Follow-up context | A follow-up remains responsive and uses provider-managed context. |
| `B03` | Provider history | Existing provider-managed history is visible and reopens where supported. |
| `B04` | Long conversation | Repeated turns do not produce a persistent hang; any recovery is documented. |
| `B05` | Clipboard | Expected copy and paste work only after user action. |
| `B06` | File upload | The provider's picker accepts a non-sensitive small sample file where supported. |
| `B07` | File download | Download behavior is visible and expected. |
| `B08` | Permissions | Microphone, camera, notifications, and similar requests remain explicit and user-controlled. |
| `B09` | External navigation | A non-auth unrelated link has predictable, recoverable handling. |
| `B10` | Reload | The page reloads with provider-managed session data intact where supported. |
| `B11` | Renderer recovery | A failed or restarted WebView can be recreated without restarting the desktop app. |
| `B12` | Purchase boundary | A known upgrade, subscription, billing, or checkout route cannot complete inside the lab. |

## Gate C — lightweight observations

Record observations after cold start, page settlement, answer streaming, ten ordinary turns, renderer restart, and a ten-minute inactive period:

- native window time-to-visible;
- provider page time-to-usable;
- WebView process count;
- aggregate private memory;
- approximate WebView CPU percentage;
- renderer-unresponsive or process-failure events;
- whether output only appears after a manual reload.

One-machine measurements are not public performance claims. They are evidence for later target calibration.

## Classification

| Status | Meaning |
| --- | --- |
| `Verified` | Required tests pass in the recorded environment with no prohibited workaround. |
| `Limited` | Core use works but a material limitation is documented. |
| `Experimental` | Evidence is incomplete, unstable, or insufficiently repeated. |
| `ExternalOnly` | Required authentication or core use cannot operate safely inside WebView2. |
| `Disabled` | A security, policy, or compatibility issue prevents the provider from being offered. |

The final result must state the environment and every `PASS`, `FAIL`, `BLOCKED`, `LIMITED`, or `NOT RUN` outcome. It must not turn a candidate URL into a support claim.
