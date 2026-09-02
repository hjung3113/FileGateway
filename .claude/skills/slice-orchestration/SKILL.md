---
name: slice-orchestration
description: >-
  Run one implementation slice (a GitHub issue scoped to already-closed design/policy)
  end-to-end via Orca: decompose into a dependency-aware task DAG, dispatch to worker
  agent(s) in Orca worktrees, independently re-verify build/test as CONDUCTOR, get a
  scoped independent-model review, apply fixes, push, and open the PR. Use when the user
  says "이 이슈 오케스트레이션으로", "구현하고 리뷰해서 PR 올려", or names a GitHub issue /
  slice to implement in the current repo. Project-agnostic — adapts to whatever
  design docs and model-routing convention the current repo actually has.
---

# Slice Orchestration

A repeatable procedure for taking one GitHub issue from "design already closed" to
"merged PR", where CONDUCTOR (this session) orchestrates, worker agents implement, and
CONDUCTOR independently verifies before anything gets pushed. Works across repos —
it does not assume a specific project's doc layout, issue-numbering scheme, or model
names; resolve those from the current repo at the start of each run (step 0).

## 0. Preconditions — resolve project-specific facts first

Before dispatching anything, establish, from the current repo (not from memory of a
prior project):

- **Design/policy source of truth**: check the repo's own root docs pointer (e.g.
  `docs/INDEX.md`, `AGENTS.md`, `CONTRIBUTING.md`) for where closed design decisions and
  open questions live. The semantic policy for this slice must already be closed there
  or in a handoff/decisions doc. If it is not closed, stop and follow the project's
  design-gate rule instead of this skill — do not dispatch implementation of an
  undecided semantic policy.
- **Model-routing convention**: check `AGENTS.md` for an explicit policy section on which
  runtime/model/effort to use for implementation vs. review. If none exists, look for
  precedent in a handoff doc (e.g. `HANDOFF.md`) describing what this project has
  actually used before, and follow that. If neither exists, ask the user once with
  `AskUserQuestion` and record the answer (e.g. append it to `AGENTS.md` or the handoff
  doc) so future runs don't re-ask. The one constant across projects: **implementation and
  review must run in separate sessions on different models** — never let CONDUCTOR
  implement and then review its own code, and never let one worker seat both implement
  and review the same change.
- Confirm the base branch is green (build + test) before branching off it.

## 1. Decompose into a task DAG — but don't force parallelism

Read the target issue(s) and whatever doc sections they cite as authoritative scope.

Before splitting into parallel worker tasks, check whether the work actually has
independent seams (separate modules touching separate files) or whether it's one
cohesive area with internally coupled logic (same file(s), shared state, or one change
that blocks the meaning of another). Forcing tightly-coupled work into parallel workers
on the same file produces merge conflicts and partial-completion states that can't be
verified until reassembled — worse than one sequential track. When genuinely unsure, ask
the user with `AskUserQuestion` rather than guessing; this is a real cost/correctness
tradeoff, not a formality.

The DAG, once decided, is usually this shape regardless of how many parallel
implementation nodes it has:

```text
[issue body / plan] -> [worktree(s) + implementation dispatch] -> [CONDUCTOR independent
build/test verify] -> [scoped review dispatch] -> [fix round(s), same worker or CONDUCTOR
direct] -> [CONDUCTOR final verify] -> [push + PR] -> [handoff doc update]
```

Independent implementation nodes (when the work genuinely splits) fan out in parallel;
everything downstream of "CONDUCTOR independent verify" for a given node is sequential
for that node.

## 2. Open the GitHub issue (skip if it already exists)

One issue per slice (or sub-slice, following whatever split precedent this repo's issue
history already uses). The issue body must:

- Name the exact doc sections that are authoritative for this scope, and state plainly
  whether this is wiring already-closed rules or new semantic policy — this keeps the
  worker from improvising when it's the former.
- Point at an existing sibling file to use as an implementation template, if one exists.
- Name whatever acceptance-criteria doc/section defines the completion bar for this repo,
  if one exists.
- State the repo's actual completion bar (build clean, full test suite green, no
  regression in prior work) — don't invent a project-specific equivalence check that
  isn't this repo's own.

```bash
gh issue create --title "<topic>" --body "$(cat <<'EOF'
...
EOF
)"
```

## 3. Create the worktree and dispatch implementation

```bash
orca repo list --json   # find this repo's id once, reuse it
orca worktree create --repo id:<repoId> --name issue-<n>-<topic> --no-parent --json
orca terminal create --worktree "id:<repoId>::<worktreePath>" --title "issue-<n>-impl" \
  --command '<implementation runtime/model/effort from step 0>' --json
orca terminal wait --terminal <handle> --for tui-idle --timeout-ms 60000 --json
orca terminal send --terminal <handle> --text "<full task brief>" --enter --json
```

Pick runtime/model/effort per task size and urgency (step 0's convention), not by
default — a small, well-scoped fix doesn't need the same effort tier as a large slice.

The task brief sent to the worker must include:

- Read the project's `AGENTS.md` first; no invented semantic policy, no unrelated
  refactoring, no scope creep beyond the issue.
- The exact rule text from the closed docs, not just a section pointer — workers drift
  when only given a number.
- Which existing file to use as the wiring template, and which existing logic to
  **reuse** rather than reimplement, if applicable.
- The acceptance criteria for this issue, and where to record that they're met.
- Explicit instruction: commit but do not push, do not open the PR, do not touch the
  handoff doc (CONDUCTOR owns it) — these stay CONDUCTOR-only to avoid worker/CONDUCTOR
  collisions on shared state.
- Explicit instruction to report the real build/test pass count or say plainly that the
  sandbox blocked execution, rather than claiming an unobserved result. Worker sandboxes
  can fail to run the test suite for environment reasons (e.g. socket-bind permission
  denial) — this is a recurring failure mode worth calling out up front, not something to
  paper over.

## 4. Wait for the worker

`orca terminal wait --for tui-idle` fires on transient pauses between a worker's own tool
calls, not just true completion — a single wait call is not reliable for a multi-step
task. Poll: re-wait in a loop, and treat the terminal as actually done only when
`orca terminal show`'s `title` field has no spinner glyph (the title starts with a
braille spinner character while genuinely working). Don't just take the first idle event
at face value.

## 5. CONDUCTOR independently verifies — always, before anything downstream

Never trust a worker's self-reported pass count as the basis for merging. In the
worker's own worktree, run this repo's actual build/test commands (see step 0 or the
repo's own docs/README for the exact commands), then:

```bash
cd <worktreePath>
git log --oneline -5   # confirm the worker actually committed
git diff <base>..HEAD --stat
```

If build/test fails or the worker reports a sandbox block, CONDUCTOR fixes/re-verifies
directly in that worktree — never merge on the worker's word alone.

## 6. Scoped review dispatch

Use the review runtime/model/effort from step 0 — a different model from the one that
implemented. **Always state the review scope explicitly** in the dispatch prompt — e.g.
"review only whether this diff matches the issue body and commit message intent; do not
re-audit unrelated repo docs or policy" — otherwise the reviewer tries to re-litigate the
whole project's closed decisions.

```bash
orca terminal create --worktree "id:<repoId>::<worktreePath>" --title "issue-<n>-review" \
  --command '<review runtime/model/effort from step 0>' --json
```

Sanity-check the CLI's actual reasoning-effort flag before relying on it (flag names and
accepted values vary by tool/version — a wrong flag can fail fast and silently strand the
session at a bare shell prompt; recover by clearing and relaunching before resending the
task). If the review tool wraps its final answer in a code fence and the host reports it
as unparseable, the underlying content is usually still valid — recover it from the raw
agent log and treat it as the canonical review rather than blindly redispatching.

## 7. Apply fixes

Real findings go back to the same worker (or CONDUCTOR fixes directly for a one-line
patch). If two consecutive fix rounds from the same origin fail to resolve the issue
correctly, stop dispatching blind retries — diagnose first: name the specific wrong
assumption in the prior attempt, name a passing analog case that shows the correct
behavior, then dispatch one integrated fix describing exactly what changed and why.

After every fix round, CONDUCTOR re-verifies build/test independently (step 5) before
considering the round closed.

**Review round budget**: don't chase every nit through unlimited rounds. Once the
findings from a review round are limited to non-blocking polish (naming, minor
duplication, nice-to-have hardening) rather than a correctness/contract violation, stop
and report the remaining items rather than dispatching another round — match the
scrutiny to the actual risk, not to the process's own momentum. If the user has given an
explicit "good enough" signal for this session (e.g. "don't over-harden this"), that
signal governs future rounds without needing to re-ask.

**Do not default to fail-closed / all-or-nothing behavior when designing or reviewing a
fix**, unless the issue or an explicit requirement calls for it. A batch operation over
many independent items (definitions, files, records) should isolate and report a bad item
rather than failing the entire batch because of it, unless there's a real reason the
whole batch is invalid when one item is bad. When a fix's natural shape is "one bad input
aborts everything" or "one wrong setting blocks all otherwise-valid results," treat that
as a candidate defect to flag or fix, not a safety feature to preserve by default.

## 8. Push and open the PR

```bash
cd <worktreePath>
git push -u origin <branch>
gh pr create --title "..." --body "$(cat <<'EOF'
## Summary
...
## Verification
- build: <result>
- tests: N/N passed
Closes #<n>
EOF
)"
```

Only merge after the user confirms, unless they've explicitly pre-authorized merging in
this session.

## 8.5 Check for review comments before merging — every time

Opening the PR is not the end of the review loop. A repo bot and/or the human owner can
both leave findings on the PR after it opens, independently of the step-6 dispatch.
Before merging, always run:

```bash
gh pr view <n> --json comments,reviews
gh api repos/<owner>/<repo>/pulls/<n>/comments   # inline review comments
```

Verify each finding against the actual code and docs before accepting it (don't assume a
human reviewer is automatically right, and don't dismiss a bot finding without checking)
— then go back through steps 7 (fix) → 5 (independent verify) → push, reply to the review
comments summarizing what changed and why, and only merge after that. If a finding
reveals a genuine fork in doc interpretation (not just a code bug), check whether the
relevant doc section already states an explicit exception before escalating to the user
or a decisions doc — often the apparent ambiguity resolves this way.

## 9. Update the handoff doc and clean up

Append a dated section to this repo's handoff doc (don't create a separate dated handoff
file if one canonical doc already exists — check for it first). Then:

```bash
orca worktree rm --worktree "id:<repoId>::<worktreePath>" --force --json
git branch -d <branch>   # after merge, from the main worktree
git push origin --delete <branch>
```
