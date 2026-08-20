# Gemini WebView2 Feasibility Test Plan

## Purpose

This plan determines whether Gemini can be supported honestly inside the AI Dock WinUI 3 + WebView2 shell. A passing page load is not enough: authentication, session persistence, normal chat workflows, recovery, resource behavior, and the purchase boundary must each be tested.

The result is a compatibility finding, not a provider-support claim. Gemini remains `Experimental` until all required gates pass.

## Privacy and safety boundary

- The tester enters all account details manually.
- The harness must not inspect or store DOM content, credentials, cookies, tokens, prompts, responses, request bodies, payment data, or full URLs.
- Diagnostic events stay in memory and contain only event type, time, safe origin, result category, permission kind, or process-failure category.
- Do not spoof User-Agent, import browser cookies, intercept tokens, call private APIs, or automate sign-in.
- Use a dedicated non-sensitive test conversation and non-sensitive sample files.
- Stop before completing any purchase. A purchase flow must not complete inside the harness.

## Test environment record

Record only:

- date and tester-assigned run ID;
- Windows edition/build and architecture;
- .NET, Windows App SDK, and WebView2 Runtime versions;
- account class (`Personal` or `Workspace`) without address or identifier;
- region at country level;
- profile mode (`Fresh` or `Persistent`);
- build commit.

Current reference environment at harness creation:

- Windows build: `10.0.26200`, x64;
- .NET SDK: `10.0.400`;
- WebView2 Runtime: `151.0.4129.93`;
- Windows App SDK dependency: `2.2.0`.

## Gate A — fatal authentication feasibility

Run these cases first with a Fresh profile. If embedded authentication is blocked by Google or requires a prohibited workaround, stop Gate B and classify Gemini as `ExternalOnly` or `Limited`.

| ID | Case | Pass condition | Evidence allowed |
| --- | --- | --- | --- |
| GEM-A01 | Start with Fresh profile | Gemini origin loads without harness crash | Sanitized navigation result and screenshot with account details hidden |
| GEM-A02 | Open sign-in manually | The expected Google sign-in flow starts | Safe origins and tester observation |
| GEM-A03 | Complete sign-in manually | Account reaches Gemini without UA spoofing, cookie import, token interception, or private APIs | Pass/fail note only; no credential capture |
| GEM-A04 | Popup and redirect handling | Required popup/redirect returns to Gemini without being stranded | Safe origin transitions and tester observation |
| GEM-A05 | Restart WebView | Signed-in session survives WebView recreation using the same Persistent profile | Pass/fail note |
| GEM-A06 | Restart application | Signed-in session survives a full app restart using the same Persistent profile | Pass/fail note |

Gate A result:

- `Pass`: proceed to Gate B.
- `Blocked by provider`: do not bypass; record `ExternalOnly` candidate.
- `Intermittent or account-specific`: record `Limited`; repeat with a second allowed account class before any support claim.

## Gate B — core conversation and browser behavior

| ID | Case | Pass condition |
| --- | --- | --- |
| GEM-B01 | New text conversation | Prompt submits and streamed answer becomes visible without reload |
| GEM-B02 | Continue conversation | Follow-up retains context and remains responsive |
| GEM-B03 | History | Conversation appears in history and reopens after navigation |
| GEM-B04 | Long conversation | Repeated turns do not create persistent UI hangs; recovery is documented if a renderer becomes unresponsive |
| GEM-B05 | Clipboard | Copy and paste work only after expected user action/prompt |
| GEM-B06 | File upload | Native picker opens; a non-sensitive small file uploads successfully |
| GEM-B07 | File download | Expected download behavior is clear and does not expose a silent unsafe path |
| GEM-B08 | Permissions | Microphone/camera/notification requests remain explicit and user-controlled |
| GEM-B09 | Popup/external link | Non-auth external navigation behavior is predictable and recoverable |
| GEM-B10 | Reload | Current conversation recovers after reload without duplicate submission |
| GEM-B11 | Renderer failure recovery | A failed WebView can be recreated without restarting the desktop app |
| GEM-B12 | Purchase boundary | Known upgrade/subscription flow cannot complete inside the harness |

## Gate C — lightweight performance observations

Measure after cold start, after Gemini settles, during answer streaming, after ten ordinary turns, after WebView restart, and after a 10-minute inactive period:

- time to native window;
- time to usable Gemini page;
- WebView process count;
- total WebView working set;
- approximate WebView CPU percentage;
- renderer-unresponsive or process-failure events;
- whether the answer appears only after a manual reload.

Do not turn one-machine observations into public performance promises. Use them to set later PRD thresholds.

## Result classification

- `Verified`: all required gates pass in the recorded environment with no prohibited workaround.
- `Limited`: core use works but a documented account, permission, popup, recovery, or performance limitation remains.
- `Experimental`: incomplete evidence, unstable behavior, or insufficient repeat runs.
- `ExternalOnly`: required authentication or core use cannot safely operate inside WebView2.

The final report must list each case as `PASS`, `FAIL`, `BLOCKED`, or `NOT RUN`, state what was manually verified, and keep screenshots free of account identifiers and conversation content.
