# AI Dock Development Log

This is the durable development record for implementation decisions, verified behavior, limitations, and open work. It is not a release changelog. Planned behavior must not be presented as shipped or provider-compatible behavior.

## 2026-08-20 — Initial multi-provider manual sweep

### Scope and evidence

- One manually operated sweep covered Grok, ChatGPT, Claude, DeepSeek, Doubao, Qwen Studio, and GLM using persistent provider profiles and WebView2 Runtime `151.0.4129.93`.
- Evidence consists of the user's direct observations and a sanitized in-memory event log containing event categories and origins only. The exact repository commit, Windows build, account classes, region, prompt text, and response text were not recorded for this sweep.
- The results are environment-scoped and incomplete. None of these providers becomes `Verified` from this run.

### Recorded outcomes

1. **Grok is `Limited`, with root cause open.** X sign-in returned successfully and the same-profile WebView recreation retained login. Conversation submission completed and user-initiated copy returned the reply text, but the reply itself rendered blank. A normal Edge or Chrome comparison is still required before attributing this to WebView2. The Grok purchase page remained reachable.
2. **ChatGPT is `Experimental`.** Initial sign-in and basic use were reported normal, including navigation through Google authentication origins. Its purchase page remained reachable, so the tested purchase boundary failed and requires narrow route discovery.
3. **Claude is `Experimental`.** Google sign-in opened a separate small popup and returned successfully. Plan content is present inside the provider page. AI Drawer will not inspect or alter the DOM to hide it; the actual checkout transition remains untested.
4. **DeepSeek, Doubao, Qwen Studio, and GLM are `Experimental`.** Initial page, sign-in where exercised, and basic use were reported normal, but required feature, persistence, recovery, resource, and purchase cases remain incomplete.
5. **Do not equate a subscription panel with completed checkout.** Doubao's informational subscription modal remains visible. The native boundary should act when a reviewed checkout, payment, recharge, or billing transition is attempted, not by manipulating provider-owned page content.

### Regional-entry decisions

- Qwen Studio (`chat.qwen.ai`) and Tongyi Qianwen (`tongyi.aliyun.com/qianwen`) are separate international and China candidates.
- GLM / 智谱清言 (`chatglm.cn`) and Z.ai (`chat.z.ai`) are separate China and international candidates.
- Each regional candidate receives an independent persistent profile and compatibility result. Evidence and login state do not transfer between regional sites.
- Existing profile IDs `doubao`, `qwen`, and `glm` are preserved so the tester's local D-drive sessions are not orphaned. Only the new regional candidates receive new IDs.
- An international Doubao-family entry remains open. Cici currently redirects toward Dola, so brand relationship, regional availability, and the stable official chat entry must be reviewed before hard-coding a candidate.

### Navigation observations

- Grok authentication used sanitized origins including `accounts.x.ai`, `x.com`, `auth.grok.com`, `auth.grokusercontent.com`, `auth.x.ai`, and `auth.grokipedia.com`.
- ChatGPT navigation included `auth.openai.com` and Google account origins. A permission request was logged as numeric kind `13`; its semantic permission name was not inferred.
- Claude requested Google authentication and `claude.com` popups.
- Doubao authentication used `accounts.feishu.cn` and returned to `www.doubao.com`.
- Transient `ConnectionAborted` events during Grok and Doubao redirects were followed by successful navigation and are observations, not standalone compatibility failures.

### Settings data-reset decision

- The production settings page will distinguish cache cleanup from destructive website-data reset. `Clear cache` is intended to preserve sign-in; `Reset website data` clears the selected WebView2 profile's cookies, storage, permissions, and cache and therefore signs that local workspace out.
- Reset scope follows the profile boundary: one workspace first, optionally all workspaces for one provider, and a separately confirmed global reset for all AI Drawer web profiles.
- All affected WebViews must be disposed before reset. Cleanup uses WebView2 profile/data APIs without reading cookies, tokens, page content, prompts, or responses.
- The confirmation text must state that local sign-in is removed but the provider account and provider-hosted conversation history are not deleted. Re-authentication may make that history visible again.
- This is a requirement for the production settings experience, not an M0 Compatibility Lab feature.

## 2026-08-20 — M0 provider-matrix harness foundation

### Scope

- The Gemini-only feasibility harness was extended into a manual test harness for the initial PRD provider matrix: ChatGPT, Claude, Gemini, Grok, DeepSeek, Doubao / 豆包, Qwen / 通义千问, and GLM / 智谱清言.
- This is tooling and documentation work only. No new manual provider result was recorded in this change.

### Implementation decisions

1. **Keep one live WebView.** The lab remains one native window and one WebView at a time; M0 does not implement the production multi-workspace or bounded multi-WebView lifecycle.
2. **Keep candidates data-driven.** Each candidate supplies a display name, official test entry URL, local profile key, compatibility status label, and only reviewed purchase-route rules.
3. **Separate profiles by provider.** Persistent and fresh profiles are nested under a provider-specific directory. A fresh run receives a millisecond timestamp to avoid collisions between successive manual tests.
4. **Do not invent payment policy.** Gemini retains the routes manually observed in the prior run. Other providers display that no known purchase route is configured and require manual verification before a block rule is added.
5. **Permit deliberate switching.** Ending a test closes the current WebView before the tester changes provider or profile, avoiding cross-provider profile reuse in the lab.
6. **Preserve diagnostics and privacy behavior.** Event messages remain sanitized and in memory only. Resource sampling continues to aggregate private memory without reading page content.

### Documentation

- Added a common provider test plan and compatibility matrix with the existing Gemini evidence transcribed as `Experimental`.
- Retained the Gemini-specific plan and original run record as historical evidence.

## 2026-08-20 — Gemini WebView2 Milestone 0 feasibility run

### Scope and repository state

- Goal: determine whether the current Gemini web experience is viable inside a minimal WinUI 3 + WebView2 desktop shell before building the polished MVP.
- Branch: `test/gemini-webview2-feasibility`.
- Base commit: `ec976a5` (`docs: establish project requirements and workflow`).
- Remote `master` was in sync before the branch was created.
- This run covers one Windows machine and one manually operated account session. It is not a universal Gemini support claim.
- The user requested that this work remain local for now. No push or pull request is part of this log entry.

### Environment

| Item | Recorded value |
| --- | --- |
| OS | Windows build `10.0.26200`, x64 |
| .NET SDK | `10.0.400` |
| Windows App SDK package | `2.2.0` |
| WebView2 Runtime | `151.0.4129.93` |
| Build configuration | Debug, x64 |
| Account class | Not recorded; do not infer |
| Region | Not recorded; do not infer |

The machine initially had .NET runtimes but no .NET SDK or Visual Studio installation. The Microsoft .NET CLI route was selected because it is sufficient for this compatibility harness and avoids installing the full IDE.

Developer Mode was enabled only to launch the locally built packaged debug application. This is a development-machine requirement, not a requirement for a future Microsoft Store installation.

### Storage decisions

- .NET SDK: `D:\DevTools\dotnet`.
- .NET CLI state: `D:\DevTools\dotnet-cli-home`.
- NuGet package cache: `D:\DevTools\nuget-packages`.
- Gemini test profiles: `D:\AI Dock TestData\Gemini`.
- Build output remains under the repository on E drive.
- The debug package identity is registered by Microsoft's WinApp CLI; the harness is not installed through a normal end-user installer.
- These are test-machine paths only. A formal release should use normal Windows or Microsoft Store installation and application-data conventions unless a later product decision changes that behavior.

### Implementation decisions

1. **Use the accepted product stack.** The harness uses C#, .NET 10, WinUI 3, Windows App SDK, WebView2, and packaged debug identity.
2. **Keep the harness deliberately small.** It contains one native window, one page, and one WebView2. No MVVM framework, dependency injection, provider abstraction, database, backend, DOM automation, or private provider API was introduced.
3. **Separate persistent and disposable profiles.** `Persistent` reuses a stable user-data folder for session tests. `Fresh disposable` creates a timestamped folder for clean-profile tests.
4. **Keep provider data provider-owned.** The harness does not build a second conversation cache. Gemini history is loaded by Gemini after its account session is restored.
5. **Do not bypass authentication controls.** No User-Agent override, browser-cookie import, token interception, private API, or automated sign-in is used.
6. **Preserve the privacy boundary.** The harness does not inspect the DOM and does not record credentials, cookies, tokens, prompts, responses, request bodies, payment data, or full URLs.
7. **Use sanitized in-memory diagnostics.** Navigation, popup, permission, and process-failure events are shown only in memory. URI text is reduced to scheme, host, and non-default port.
8. **Keep permissions user-controlled.** Permission requests are observed and logged by category, but the harness does not silently grant them.
9. **Block known purchase entry points inside the harness.** Known Google payment/subscription origins and Gemini upgrade/subscription paths are cancelled before internal navigation or popup creation. This is a maintained policy, not a permanent guarantee against provider URL changes.
10. **Measure WebView resources without reading page content.** Process count, approximate CPU, and aggregate private memory are sampled from WebView2 process information.
11. **Correct the initial memory metric.** The first build summed per-process working sets, which can double-count shared pages. After the test it was changed to aggregate `PrivateMemorySize64`. The original `702.8 MB` screenshot observation is retained below but is invalid for product memory claims.

### Build evidence

The harness built successfully before the manual run:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

The app then launched successfully through the Windows App Development CLI after Developer Mode was enabled.

### Manual test results

Evidence comes from the user's direct interaction and a screenshot of the signed-out harness. The repository does not store the screenshot, account identifiers, or conversation content.

| Test | Result | Evidence and boundary |
| --- | --- | --- |
| GEM-A01 Gemini signed-out page load | PASS | `https://gemini.google.com` completed successfully in WebView2. The run used `Persistent`; the explicit `Fresh disposable` variant remains not run. |
| GEM-A02 Start Google sign-in | PASS | User started the expected sign-in flow manually. |
| GEM-A03 Complete embedded sign-in | PASS | User reported successful login without a prohibited workaround. |
| GEM-A04 Return to Gemini | PASS | User returned to Gemini and could access the signed-in experience. |
| GEM-A05 Recreate WebView with same profile | PASS WITH EVIDENCE NOTE | The user followed the `Restart WebView` instruction and reported that login and history remained. Their wording called it a refresh, so the exact clicked control was not independently observed. |
| GEM-A06 Restart full application | PASS | The window was closed, a new process was launched, and the user reported that login state remained without signing in again. |
| GEM-B01 New conversation and visible response | PASS | User reported normal submission and response; no response content was collected. |
| GEM-B02 Follow-up context | PASS | User reported the follow-up response was normal. |
| GEM-B03 Account history | PASS | History loaded after login and again after session restoration. This is Gemini account history, not AI Dock local caching. |
| GEM-B04 Long conversation | NOT RUN | The two-turn check is not a long-conversation or renderer-stress test. |
| GEM-B05 Clipboard | PASS | User reported copy/paste worked normally. |
| GEM-B06 File upload via picker | PASS | User reported attachment selection and upload worked. |
| File drag-and-drop upload | LIMITED | Dragging a file onto the WebView did not work. The supported MVP path is the page's attachment picker unless a later focused drag/drop implementation is approved. |
| GEM-B07 File download | NOT RUN | No download flow was tested. |
| GEM-B08 Microphone permission prompt | PASS | Sanitized event: `permission-request kind=Microphone origin=https://gemini.google.com`. A visible prompt appeared and the user chose Deny. Post-denial media behavior was not separately exercised. |
| GEM-B09 Generic external link behavior | NOT RUN | Authentication and subscription popup behavior were observed, but a generic unrelated external link was not tested. |
| GEM-B10 Reload recovery | PASS | User reported normal response, retained login, and retained conversation after Reload. |
| GEM-B11 Forced renderer/process failure | NOT RUN | WebView recreation was tested; an actual or simulated renderer/process failure was not. |
| GEM-B12 Known purchase boundary | PASS | Sanitized event: `purchase-popup-blocked https://one.google.com`. The subscription popup did not open and no purchase page was entered. |

### Performance observations

The signed-out page screenshot showed:

- 6 WebView processes;
- approximately 1.2% sampled CPU at that moment;
- 702.8 MB from the original aggregate working-set implementation.

The 702.8 MB value must not be used as a product result because shared memory may be counted in more than one process. The implementation now reports aggregate private memory, but that corrected metric has not yet been measured in a fresh run. Cold start time, time to usable page, streaming CPU, ten-turn behavior, inactive behavior, and post-recovery behavior remain unmeasured.

### Compatibility conclusion

Gemini is **technically viable and remains Experimental** on the tested environment.

The previously identified fatal risk—Google rejecting embedded authentication—did not occur in this run. Authentication, session persistence, ordinary text chat, account history, clipboard, picker-based file upload, explicit microphone permission, reload recovery, and the tested subscription boundary all worked.

This does not establish official Google approval or universal compatibility. Provider behavior can vary by account type, region, policy, and future web changes. Public compatibility language must remain environment-scoped until the remaining gates and repeat runs pass.

### Known limitations and open work

- Run the explicit `Fresh disposable` profile case.
- Re-measure private memory and CPU after the metric correction.
- Perform a longer conversation/stress run and observe whether answers ever require reload.
- Test file download behavior.
- Test generic external-link routing.
- Test a real or controlled renderer/process failure and recovery path.
- Test inactive/suspend behavior and resource reclamation.
- Repeat authentication with any supported account classes that materially differ, without recording identifiers.
- Repeat on at least one other supported Windows/WebView2 environment before a public support claim.
- Add regression coverage for URL sanitization and known purchase-route matching.
- Treat file drag-and-drop as a documented limitation unless a focused implementation is later approved.
- Revalidate purchase routes when Gemini or Google One navigation changes.

### Decisions deliberately deferred

- Whether Gemini should ship as `Verified`, `Limited`, or `ExternalOnly` in the first public release.
- Whether a provider-supported system-browser OAuth handoff exists and is needed as a fallback.
- General external-navigation allowlist policy beyond the tested authentication and subscription cases.
- Formal installer/runtime packaging and Store distribution details.
- Multi-provider WebView lifecycle and memory budget thresholds.
- Whether native drag-and-drop bridging is worth the added complexity for MVP.
