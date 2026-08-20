# Provider Compatibility Matrix

> Status: work in progress. This matrix contains manual test evidence only. `Not tested` and `Experimental` are not support claims.

## Environment records

| Run ID | Date | Commit | Windows / WebView2 | Region | Account class | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `gemini-20260820-01` | 2026-08-20 | `d9aa9be` | Windows `10.0.26200` x64 / WebView2 `151.0.4129.93` | Not recorded | Not recorded | Persistent profile; one manually operated session. |

## Provider status

| Provider | Status | Last evidence | Known limitation / next gate |
| --- | --- | --- | --- |
| ChatGPT | Not tested | — | Run A01–C observations. |
| Claude | Not tested | — | Run A01–C observations. |
| Gemini | Experimental | 2026-08-20 | Fresh profile, long conversation, download, generic external link, renderer failure, corrected metrics, and repeat environment remain open. File drag-and-drop was limited. |
| Grok | Not tested | — | Run A01–C observations. |
| DeepSeek | Not tested | — | Run A01–C observations. |
| Doubao / 豆包 | Not tested | — | Run A01–C observations. |
| Qwen / 通义千问 | Not tested | — | Run A01–C observations. |
| GLM / 智谱清言 | Not tested | — | Run A01–C observations. |

## Gemini recorded results

| Case | Result | Evidence boundary |
| --- | --- | --- |
| A01–A04 | PASS | Signed-out page load and embedded manual sign-in returned to Gemini without a prohibited workaround. |
| A05 | PASS WITH EVIDENCE NOTE | The user reported login and history remained after the same-profile WebView recreation instruction; the exact clicked control was not independently observed. |
| A06 | PASS | Login remained after a full application restart. |
| B01–B03 | PASS | Ordinary text conversation, a follow-up, and provider-managed history worked. No conversation content was collected. |
| B04 | NOT RUN | Two turns are not a long-conversation test. |
| B05 | PASS | Clipboard worked after user action. |
| B06 | PASS / LIMITED | Picker-based file upload worked; file drag-and-drop did not. |
| B07 | NOT RUN | Download not exercised. |
| B08 | PASS | A microphone request was displayed and manually denied. Post-denial media behavior was not tested. |
| B09 | NOT RUN | Generic unrelated external navigation was not exercised. |
| B10 | PASS | Reload retained login and conversation state in the reported run. |
| B11 | NOT RUN | Same-profile recreation is not a forced renderer-failure test. |
| B12 | PASS | A known Google One subscription popup was blocked; the event log showed only its sanitized origin. |

## Per-provider result template

Copy this section for each new recorded run. Do not include account identifiers, prompts, responses, screenshots with personal details, full URLs, cookies, tokens, or payment data.

| Case | Result (`PASS` / `FAIL` / `BLOCKED` / `LIMITED` / `NOT RUN`) | Sanitized evidence and note |
| --- | --- | --- |
| A01 |  |  |
| A02 |  |  |
| A03 |  |  |
| A04 |  |  |
| A05 |  |  |
| A06 |  |  |
| B01 |  |  |
| B02 |  |  |
| B03 |  |  |
| B04 |  |  |
| B05 |  |  |
| B06 |  |  |
| B07 |  |  |
| B08 |  |  |
| B09 |  |  |
| B10 |  |  |
| B11 |  |  |
| B12 |  |  |
