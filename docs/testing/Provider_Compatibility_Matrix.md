# Provider Compatibility Matrix

> Status: work in progress. This matrix contains manual test evidence only. `Not tested` and `Experimental` are not support claims.

## Environment records

| Run ID | Date | Commit | Windows / WebView2 | Region | Account class | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `gemini-20260820-01` | 2026-08-20 | `d9aa9be` | Windows `10.0.26200` x64 / WebView2 `151.0.4129.93` | Not recorded | Not recorded | Persistent profile; one manually operated session. |
| `matrix-20260820-01` | 2026-08-20 | Not recorded | Windows build not re-recorded / WebView2 `151.0.4129.93` | Not recorded | Not recorded | Persistent provider profiles; one manually operated sweep. Sanitized origin-only event log and user observations were recorded. |

## Provider status

| Provider | Status | Last evidence | Known limitation / next gate |
| --- | --- | --- | --- |
| ChatGPT | Experimental | 2026-08-20 | Initial sign-in and basic use worked, but the purchase page remained reachable. Complete the remaining gates and purchase-route discovery. |
| Claude | Experimental | 2026-08-20 | Google sign-in used a separate small popup and returned successfully. Inline plan content cannot be removed without crossing the DOM boundary; checkout behavior remains untested. |
| Gemini | Experimental | 2026-08-20 | Fresh profile, long conversation, download, generic external link, renderer failure, corrected metrics, and repeat environment remain open. File drag-and-drop was limited. |
| Grok | Limited | 2026-08-20 | X sign-in worked and same-profile WebView recreation retained login. A reply existed and could be copied, but its text rendered blank in the tested environment. Browser comparison is pending; purchase page remained reachable. |
| DeepSeek | Experimental | 2026-08-20 | International chat entry and basic use were reported normal. Subscription surface was not observed; detailed gates remain open. |
| Doubao / 豆包 (China) | Experimental | 2026-08-20 | Sign-in and basic use were reported normal. Subscription information appeared as an in-page modal; checkout boundary remains untested. |
| Qwen Studio (International) | Experimental | 2026-08-20 | Initial page and basic use were reported normal; detailed gates remain open. |
| Tongyi Qianwen / 通义千问 (China) | Not tested | — | Separate regional site and profile; run A01–C observations. |
| GLM / 智谱清言 (China) | Experimental | 2026-08-20 | Initial page and basic use were reported normal; detailed gates remain open. |
| Z.ai (International) | Not tested | — | Separate regional site and profile; run A01–C observations. |
| Microsoft Copilot (Personal) | Not tested | — | Run all authentication, conversation, persistence, popup, permission, external-navigation, and purchase-boundary observations. Work/education Copilot is a separate product and is not covered. |

## Initial provider sweep recorded results

These are deliberately coarse results from one manual sweep. “Reported normal” does not fill unobserved case rows or establish `Verified` status.

| Provider | Authentication and page | Basic conversation | Session / popup | Purchase boundary | Other observation |
| --- | --- | --- | --- | --- | --- |
| Grok | PASS — X sign-in returned to Grok without a prohibited workaround. | LIMITED — submission and reply completed, but reply text rendered blank; user-initiated copy exposed the expected text. | PASS WITH EVIDENCE NOTE — same-profile WebView recreation retained login. | FAIL — the purchase page could open inside the lab. | Sanitized auth origins were observed. One transient `ConnectionAborted` was followed by a successful navigation. Root cause of blank rendering is open pending normal-browser comparison. |
| ChatGPT | PASS WITH EVIDENCE NOTE — page and sign-in were reported normal; Google authentication origins were observed. | PASS WITH EVIDENCE NOTE — basic use was reported normal. | NOT RUN as a complete persistence gate. A permission request with numeric kind `13` was observed, but the permission meaning and user decision were not recorded. | FAIL — the purchase page could open inside the lab. | Other Gate B and Gate C cases remain open. |
| Claude | PASS — Google sign-in opened a small separate popup and returned to a usable Claude page. | PASS WITH EVIDENCE NOTE — basic use was reported normal. | LIMITED — separate login popup behavior worked in this run but needs repeat coverage. | NOT RUN — plan content was visible inline; checkout was not entered. | AI Drawer must not inspect or alter the page DOM merely to hide inline plan content. |
| DeepSeek | PASS WITH EVIDENCE NOTE — international chat entry and login were reported normal. | PASS WITH EVIDENCE NOTE — basic use was reported normal. | NOT RUN as a complete persistence gate. | NOT RUN — no subscription surface was observed. | Account/site variant was described as international. |
| Doubao / 豆包 (China) | PASS — page and Feishu-based sign-in completed and returned to Doubao. | PASS WITH EVIDENCE NOTE — basic use was reported normal. | NOT RUN as a complete persistence gate. | NOT RUN — an informational subscription modal was visible, but checkout was not entered. | One transient `ConnectionAborted` was followed by a successful navigation. The informational modal is not itself blocked; a later known checkout route must be handled when identified. |
| Qwen Studio (International) | PASS WITH EVIDENCE NOTE — page and use were reported normal. | PASS WITH EVIDENCE NOTE — basic use was reported normal. | NOT RUN. | NOT RUN. | The tested entry was `chat.qwen.ai`; China site results must be recorded separately. |
| GLM / 智谱清言 (China) | PASS WITH EVIDENCE NOTE — page and use were reported normal. | PASS WITH EVIDENCE NOTE — basic use was reported normal. | NOT RUN. | NOT RUN. | The tested entry was `chatglm.cn`; Z.ai results must be recorded separately. |

### Purchase-boundary interpretation

- An informational pricing or subscription panel inside the provider page is not removed or hidden. Doing so would require page-content inspection or manipulation outside the product privacy boundary.
- A known transition into checkout, payment, recharge, or billing should be cancelled and explained by the native shell once its sanitized origin/path rule has been reviewed.
- ChatGPT and Grok currently fail the tested purchase boundary because their purchase pages remained reachable. No block patterns are added from this run because the privacy-safe log recorded only origins and did not capture the exact reviewed routes needed for a narrow rule.
- The Doubao modal and Claude inline plan content are observations, not successful checkout tests.

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
