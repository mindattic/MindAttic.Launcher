---
description: Hunt for real bugs in MindAttic.Launcher and fix every one found. "/bughunt X" searches for X minutes then fixes everything found (the time budget bounds the search, not the fixing). "/bughunt" with no argument searches exhaustively once, then fixes everything found.
argument-hint: "[minutes]"
allowed-tools: Read, Grep, Glob, Edit, Write, Bash, PowerShell, Agent
---

# /bughunt — time-boxed search, unbounded fix (MindAttic.Launcher)

Two phases, always in this order: **search**, then **fix**. The time budget in `$ARGUMENTS`
(if any) bounds only the search phase. Once search ends, fix every bug found — no matter how
long the fix phase itself takes. The budget is never a time limit on fixing.

## 1. Parse the budget

- If `$ARGUMENTS` is a plain number, that's **X minutes** for the search phase.
- If `$ARGUMENTS` is empty, there is no time budget — do one exhaustive search pass across the
  whole solution instead of clock-bounding it.
- If `$ARGUMENTS` is present but not a number, ask the user to clarify rather than guessing.

## 2. Scope

Default scope is `MindAttic.Launcher\` (the app) and `MindAttic.Launcher.Tests\` (the test
project) — the whole `MindAttic.Launcher.slnx` solution. Skip `artifacts\`, `bin\`, `obj\`, and
anything git-ignored. This is a sweep of the whole codebase, not a review of the current diff
(any uncommitted changes are in scope like everything else, not special-cased).

## 3. Search phase — find bugs, do not fix yet

Keep a running list in your own scratch notes (not a project file) of **confirmed or
highly-plausible bugs only** — each with file:line, a one-line description, and the concrete
input/state that makes it wrong. Exclude style preferences, hypothetical hardening, and anything
that's a matter of taste rather than a real defect.

What counts as a bug: crashes, incorrect logic, null/reference errors, off-by-one errors, race
conditions and other concurrency bugs, resource/connection/process leaks, swallowed or
mishandled exceptions, broken error handling, security issues (secrets outside Vault, command
injection via spawned providers, path traversal), data loss/corruption risks, and dead code paths
that silently produce wrong results. A violation of one of this project's documented Laws is
also a bug — check against `docs/BIBLE.md` (#MCO-LAW-1 through #MCO-LAW-5) and the inherited
`MindAttic.HouseRules.md` laws, e.g.:

- a failed external command (bad `--provider`, empty `RunCommand`, missing project) falling back
  silently instead of a distinct non-zero exit code (MCO-LAW-1)
- a settings/model type without `[JsonExtensionData] Extra`, or a save path that drops unknown
  keys (MCO-LAW-2)
- backup logic that only robocopies files without the `sqlcmd BACKUP DATABASE` step (MCO-LAW-3)
- a Windows Terminal scheme write that isn't idempotent — duplicates or clobbers on re-run
  (MCO-LAW-4)
- any code path that calls an LLM SDK/API directly instead of just spawning a provider CLI
  (MCO-LAW-5)
- a hardcoded credential/setting instead of resolving through MindAttic.Vault (HOUSE-LAW-3)

Technique, in rough order:

1. Run `dotnet build` (the project has `TreatWarningsAsErrors=true` and `Nullable=enable`, so
   warnings — including nullability warnings — are real signal, not noise) and `dotnet test`.
   A failing/flaky test or a build warning is free, high-confidence signal — start there.
2. Read through the source systematically, one `Services\`/`Commands\`/`Models\` file per pass.
   For independent areas, use the Agent tool to fork parallel read-only searches (Explore or
   general-purpose agents) so more ground is covered within the same wall-clock budget. Each
   fork reports findings with file:line back to you — it does not fix anything itself.
3. Grep for bug-prone C# patterns: unchecked nulls despite `Nullable=enable` warnings being
   suppressed with `!`, unawaited `async` calls, `IDisposable`/`Process`/file-handle leaks,
   `catch` blocks that swallow exceptions, string-built SQL passed to `sqlcmd`, and any secret
   or path that bypasses Vault.

If a time budget was given: keep taking search passes (broadening or deepening coverage),
checking the real wall-clock elapsed time between passes (e.g. PowerShell `Get-Date`) rather than
estimating. Stop the moment X minutes have elapsed, even mid-pass — whatever's been found so far
moves on to the fix phase.

If no budget was given: keep going until the whole solution has been covered once at reasonable
depth, then stop searching regardless of how long that took.

## 4. Fix phase — no time limit

Go through every recorded bug and fix it:

- Minimal, targeted fix for the actual defect — no unrelated refactors, no drive-by style
  changes, no new abstractions beyond what the fix requires.
- Prefer adding or extending a focused NUnit test in `MindAttic.Launcher.Tests` alongside each
  fix when practical — this repo's own convention is that done is verified, not asserted
  (HOUSE-LAW-8: every `✅` in `docs/USER_STORIES.md` cites a test).
- After each fix (or in a batch at the end, whichever is safer for the change), re-run
  `dotnet build` and `dotnet test` to confirm the fix works and nothing else regressed.
- If something recorded as a "bug" turns out, on closer inspection, not to be a real defect,
  drop it and say why rather than making a no-op change.
- If something is a real bug but unsafe to auto-fix (the correct behavior is a product decision,
  or the fix is destructive/high-risk — e.g. touches backup/Vault/credential paths), do NOT fix
  it — list it separately as "needs human input" with the reasoning.

## 5. Report

End with a short summary: how long the search ran (or that it was unbounded), how many bugs were
found, a table of bug → fix → verification status, and a separate list of anything flagged but
intentionally left unfixed. Do not commit or push automatically — leave fixes in the working tree
for the user to review.
