# MindAttic.Console — How to work here

MindAttic.Console is a single Windows binary (`MindAttic.Console.exe`) that launches and
orchestrates the whole MindAttic workspace: an interactive Spectre.Console menu plus `host` /
`commit` / `version` sub-commands. Target framework `net10.0-windows`, `win-x64`. See
[README.md](README.md) for build/run; see [docs/BIBLE.md](docs/BIBLE.md) for how to think about it.

## Codex — canonical documentation (read first)
The docs under `docs/` are the source of truth, layered. A fact lives in exactly one layer; link to
it by stable ID, never by line number.

- **L0 [docs/BIBLE.md](docs/BIBLE.md)** — what the system IS / is NOT, architecture, the Laws,
  verified state, glossary. Section IDs `{#MCO-§N}`, laws `{#MCO-LAW-n}`.
- **L1 [docs/AMENDMENTS.md](docs/AMENDMENTS.md)** — append-only change log (`MCO-A<n>`). An
  amendment **wins** over the bible; never rewrite one, supersede it.
- **L2 [docs/USER_STORIES.md](docs/USER_STORIES.md)** — stories `MCO-US-<Epic><n>`; every `✅`
  cites its verifying NUnit test.
- **rfc [docs/rfc/](docs/rfc/)** — design notes; graduate into L0+L2, then mark superseded.
- **GENERATED [docs/BIBLE.digest.md](docs/BIBLE.digest.md)** — produced by `tools/codex.ps1 digest`;
  injected at session start. **Never hand-edit.**
- **Org laws:** [`MindAttic.HouseRules.md`](../MindAttic.HouseRules.md) — inherited by reference from
  BIBLE §5. Do not restate or modify it here.

## Rules of engagement
- Status legend everywhere: `✅ done` (verified by a test/build) · `🟡 partial` · `⬜ planned` ·
  `🗑️ cut` · `living`. Mark `✅` only when a test or build proves it.
- When you change behavior, update the single owning layer and adjust links — don't duplicate facts.
- After editing any `docs/` canon, run `powershell -File tools/codex.ps1 doctor` (regenerates the
  digest and validates IDs, links, front-matter, cited tests/paths, and digest freshness). It must
  exit 0.
- Regenerate the digest with `powershell -File tools/codex.ps1 digest` whenever BIBLE §1/§3/§5/§9 or
  the latest amendment changes.

## Build & test
- Build: `dotnet build` (`TreatWarningsAsErrors=true`, `Nullable=enable`).
- Test: `dotnet test` (NUnit 4). Keep it green; cite the test name in the story you close.
- Publish: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1`.
- Versioning: whole-number, major-only (see HOUSE-LAW-1); the csproj `<Version>` is `N.0.0`.
