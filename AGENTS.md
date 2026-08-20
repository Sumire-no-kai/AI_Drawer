# Repository Working Agreement

This file applies to the entire repository. Read it before making plans, edits, commits, pushes, pull requests, or releases.

## Product source of truth

- Read `AI_Dock_PRD_v0.2.md` before product or implementation work.
- Keep verified behavior, planned behavior, and provider-specific limitations distinct.
- Do not claim provider compatibility until the applicable PRD test Gate has passed.
- Preserve the product's privacy boundary: do not read or store prompts, responses, DOM, credentials, cookies, tokens, or payment data.

## Git workflow

- Begin by inspecting the current branch, working tree, relevant history, and remote state. Preserve user-owned and unrelated changes.
- Use the simplest workflow appropriate to the change. Small documentation, configuration, typo, and similarly low-risk repository maintenance may be committed directly to `master`.
- Create a focused branch for product features, multi-file implementation work, risky fixes, experiments, architectural changes, or any change that benefits from isolated review.
- Use concise, product-focused branch, commit, and pull-request names. Do not put AI assistant or agent names in Git artifacts.
- Make coherent commits with messages that describe the actual change.
- When requested work is complete and verified, commit and push it automatically unless the user explicitly says not to do so.
- When work is performed on a branch, open or update the pull request when it is ready for review.
- Never merge a pull request without the user's explicit confirmation. The only exception is when the user has explicitly authorized autonomous merging for that specific work.
- Never force-push, rewrite shared history, discard user changes, or use destructive Git commands without explicit approval.

## Implementation discipline

- Prefer the smallest, clearest implementation that fully satisfies the requirement.
- Follow YAGNI: do not introduce abstractions, frameworks, dependencies, services, or extension points without a concrete current need.
- Keep responsibilities narrow. Avoid God classes, oversized managers, and objects that mix UI, lifecycle, persistence, navigation, and provider policy.
- Make minimal diffs. If a correct fix requires one or two lines, do not turn it into a broad refactor.
- Match existing naming, structure, formatting, and architectural boundaries before introducing a new pattern.
- Do not replace working code merely because another style is personally preferable.

## Major decisions

- Discuss major decisions with the user before implementing them.
- Provide the relevant background, constraints, realistic options, trade-offs, and a clear recommendation in plain language.
- Major decisions include framework or architecture changes, new runtime dependencies, authentication approaches, privacy-boundary changes, storage formats, update and distribution mechanisms, security policy, provider support claims, and scope changes that materially affect the PRD.
- A reversible local implementation detail that follows an accepted design is not a major decision and does not require unnecessary ceremony.

## Verification and self-review

- After implementation, run checks proportional to the risk: formatting, build, tests, focused manual verification, and packaging checks where applicable.
- Then review the final diff from a fresh perspective as if reviewing another contributor's work.
- Look specifically for correctness bugs, edge cases, lifecycle leaks, unnecessary complexity, security or privacy regressions, unintended file changes, weak error handling, and missing tests.
- Fix issues found during self-review before presenting the work.
- Re-run the relevant checks after fixes.
- Report what was verified, what was not verified, and any remaining risk. Do not present planned or inferred behavior as tested fact.

## Communication

- Lead with the outcome and use plain language.
- Keep progress updates concise and meaningful.
- Call out blockers and important assumptions early.
- Do not create process for its own sake: use branches, pull requests, and documentation when they add real safety or review value.
