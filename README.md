# MindAttic.Launcher

One Windows console for a whole workspace of repos: open any project in a colored Windows Terminal tab running Claude, Codex, Gemini or Kimi, commit and push everything at once, and back up files plus SQL databases.

![C#](https://img.shields.io/badge/C%23-.NET%2010-512BD4) ![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6) ![UI](https://img.shields.io/badge/UI-Spectre.Console-informational) ![Tests](https://img.shields.io/badge/tests-NUnit%204-green) ![Status](https://img.shields.io/badge/status-active-brightgreen)

```text
 MindAttic.Launcher                       (interactive menu, items from MainMenuCommand.cs)

   Commit and sync               commit and push changes per project or across all
   Pull                          git pull --ff-only per project or across all
 > Open Project Tab              select a project, then pick which agent CLI to open it with
   Backup                        back up MindAttic to R:\Backup\MindAttic
   Settings                      CLI development: the model each agent CLI runs with
   Status                        open a Claude tab at the workspace root with /status pre-filled
   Open Command Prompt (Admin)   open cmd as Administrator at the workspace root
   Open PowerShell (Admin)       open PowerShell as Administrator at the workspace root
   Restart                       reload this console in a new tab; other tabs are untouched
   Exit                          close this menu (other tabs are untouched)
```

A single `MindAttic.Launcher.exe` (`net10.0-windows`, published `win-x64`) built on [Spectre.Console](https://spectreconsole.net/): one interactive menu plus scriptable `host`, `commit` and `version` sub-commands. It is a console app, so the menu above is a text rendering of its items rather than a screenshot.

## Why

- Go from "I want to work on that repo" to a live agent session in two keystrokes: pick a project, pick an agent, and a titled, colored Windows Terminal tab opens at the right folder.
- Switch agents freely: Claude, Codex, Gemini and Kimi are one menu apart, and the choice is never saved, so every session starts from a fresh pick.
- See at a glance which tabs are busy: each tab title carries a busy or idle marker that the launcher keeps pinned even when the agent CLI rewrites it.
- Commit and push every dirty repo in one pass, with a commit message written for you when you do not give one.
- Get a backup you can actually restore: a `robocopy` snapshot of the workspace plus genuine, checksummed `BACKUP DATABASE` dumps of every configured SQL Server database, in one dated folder.
- New repos announce themselves: anything cloned under the workspace root is offered for the roster at startup, with a matching terminal color scheme.

## Features

- Interactive Spectre.Console menu for commit, pull, open-project, backup, model settings, status and elevated shells.
- Agent host tabs: every session runs inside `MindAttic.Launcher.exe host`, which execs the provider CLI with inherited stdio, injects any Vault-stored API key the CLI needs, and keeps the tab title pinned.
- Project discovery at startup for git repos under the workspace root, with "skip all", "never ask again" and per-repo skip.
- Per-project tab color and a matching `MindAttic-<Name>` Windows Terminal scheme, spliced into the user's WT settings without reformatting the file.
- Per-provider model picker that rewrites the `--model` flag inside the provider's run command.
- Lossless settings: unknown keys written by other MindAttic tools survive every save ([MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault) backs the store).
- Self-refreshing exe: the in-app Restart waits out the Windows file lock, republishes and relaunches; a header notice warns when the running build is behind the latest commit.
- A live Claude usage and rate-limit block above every menu redraw (read from `~/.claude/*`, cached 60 seconds).

It is not an agent: it execs a provider CLI with inherited stdio and never calls an LLM SDK or makes an LLM API call directly.

## Quick start

Prerequisites: Windows, the .NET 10 SDK, Windows Terminal (`wt`) on `PATH`, at least one agent CLI (`claude`, `codex`, `agy` or `kimi`), and for backups `robocopy` and `sqlcmd`.

```powershell
git clone https://github.com/mindattic/MindAttic.Launcher.git
cd MindAttic.Launcher
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1
.\artifacts\MindAttic.Launcher.exe
```

You should see the menu above. On first run the launcher offers every git repo directly under the workspace root for the roster; pick a color for each, then choose Open Project Tab.

## How it works

`Program.cs` wires a `Spectre.Console.Cli` `CommandApp<MainMenuCommand>` with the named sub-commands `host` and `commit` plus `version`/`--version`. Running the exe with no arguments drives the interactive menu; every named sub-command is a scriptable front door onto the same services. There is no separate code path for "menu mode" versus "CLI mode" (HOUSE-LAW-6, "one engine, many front doors").

```text
                         args
                          |
                  Spectre.Console.Cli (Program.cs)
                          |
        +-----------------+------------------+--------------+
        |                 |                  |              |
   (default)          host              commit          version
        |                 |                  |
  MainMenuCommand   HostAgentCommand   CommitCommand
        |                 |                  |
   Menus/* ------> Services/* ------> external tools
   (Spectre UI)    (logic + IO)       wt, git, robocopy, sqlcmd, provider exe
        |                 |
   Ui/* (Menu,        TitlePinner / HostInputPipeServer
   Screen, Theme)     (per-tab background loops)
        |
   Models/* (Project, AgentProvider, AppSettings)  <--> SettingsStore <--> MindAttic.Vault
```

Every external-process invocation (`wt`, `git`, `robocopy`, `sqlcmd`, an agent provider exe) is factored into an injectable service with a pure logic core (parsing, path, SQL and argv composition, idempotency checks), so the decision can be unit-tested even when the process never runs. The project's quality bar ([BIBLE section 8](docs/BIBLE.md#MCO-§8)) requires this before a story is marked done.

## Commands

All sub-commands are declared in `MindAttic.Launcher/Program.cs` and implemented in `MindAttic.Launcher/Commands/`.

| Command | Behavior | Exit codes |
|---|---|---|
| (none) | Runs `MainMenuCommand`, the interactive Spectre.Console menu. | `0` |
| `host` | Runs an agent provider inside the current tab. Options below. | see below |
| `commit` | Commits and pushes one project or every roster project. Options below. | `0` all clean or pushed; `1` at least one failure |
| `version` (alias `--version`) | Prints the assembly's informational version and the running exe's process path. | `0` |

### host

```powershell
MindAttic.Launcher.exe host --name MindAttic.Vault --provider Claude
```

Options:

- `--name <NAME>`: project name from settings (optional if `--path` is given).
- `--path <PATH>`: root the agent at an arbitrary directory instead of a registered project. Takes precedence over `--name`; this is how the Status menu item roots a session at the workspace root.
- `--title <TITLE>`: tab title (defaults to `--name`, or the directory's leaf name).
- `--provider <PROVIDER>`: provider key (`Claude`, `Codex`, `Gemini` or `Kimi` by default). Omit it to resolve to the first-listed provider.
- `--prompt <PROMPT>`: seeds the agent's first turn (pre-fills input; does not auto-submit).

Behavior: splits the resolved provider's `RunCommand` into argv (`CommandLineParser.Split`), pushes any Vault-stored credential for that provider (see [Agent providers and host tabs](#agent-providers-and-host-tabs)), starts `TitlePinner` (busy/idle tab-title watchdog) and `HostInputPipeServer` (per-tab named pipe for input injection), then starts the provider exe with inherited stdio and waits for exit.

Exit codes:

- `0`: the provider ran and exited (its own exit code is passed through).
- `1`: unknown `--name`.
- `2`: provider resolved but its `RunCommand` is empty.
- `3`: provider process failed to start.
- `4`: an explicit `--provider` key does not resolve to a configured provider.
- `64`: neither `--name` nor `--path` given.

`host` is what a Windows Terminal tab actually runs. The interactive menu never launches a provider directly; it always builds a `wt … -- MindAttic.Launcher.exe host …` command line via `WindowsTerminalLauncher` and lets `wt` fork the tab. The same `HostAgentCommand.Execute` path fires whether the tab was opened from the menu or by hand from a script.

### commit

```powershell
MindAttic.Launcher.exe commit
MindAttic.Launcher.exe commit --project MindAttic.Vault --message "Fix readme"
```

Options:

- `-p`, `--project <PROJECT>`: limit to one project (defaults to every roster project).
- `-m`, `--message <MESSAGE>`: commit message (defaults to an auto-generated summary of `git status --porcelain`).

For each target it reads git status, skips clean repos, auto-generates a message from added, modified, deleted and renamed files (capped to 200 characters, falling back to counts) when none is given, then runs `git add -A`, `git commit -m <msg>` and `git push --quiet`. Exit `1` means at least one project had a missing path, an invalid git status, or a failed add, commit or push.

## Interactive menu

Launching the exe with no arguments runs `MainMenuCommand`, which loads settings (performing a one-time legacy-file seed if needed, see [Settings and persistence](#settings-and-persistence)), then, before the first menu paint, runs the startup Discover Projects walkthrough (`DiscoverProjectsMenu`, not itself a menu item), offering to add any git repo found directly under the workspace root that is not yet in the roster or on the "never ask again" list. It also runs a one-time build-staleness check (`BuildFreshness.Check()`, which warns when the running exe is behind the latest commit in this repo) and renders a live Claude usage and rate-limit block (`ClaudeStatusService`, cached 60 seconds, read from `~/.claude/*`) above every menu redraw.

### Top level menu

| Item | Tag | Description |
|---|---|---|
| Commit and sync | `commit` | Opens `CommitMenu`: commit and push one project or all, prompting for an optional message (blank means auto-generated). |
| Pull | `pull` | Opens `PullMenu`: `git pull --ff-only` one project or all. |
| Open Project Tab | `open` | Opens `OpenProjectMenu`: pick a project, then which agent CLI to launch it with, then `ProjectActionMenu`. |
| Backup | `backup` | Runs `BackupMenu` directly: confirms source, target and database list, then runs the file and SQL backup. |
| Settings | `settings` | Opens `SettingsMenu`: the model each agent CLI runs with (only). |
| Status | `status` | Opens a host tab at the resolved workspace root using the first-listed provider, pre-filled with `/status`. |
| Open Command Prompt (Admin) | `cmd` | Opens an elevated `cmd` tab at the workspace root via UAC (`wt … -- cmd`, `Verb=runas`). |
| Open PowerShell (Admin) | `ps` | Same, but `powershell`. |
| Restart | `restart` | Launches `scripts\restart.ps1` in a fresh tab (waits for this process's PID to exit, force-republishes, then re-execs) and exits this instance. Needed because Windows locks the running exe image so it cannot republish itself in place. |
| Exit | `exit` | Closes the menu; other tabs are untouched. |

The header also shows the staleness notice and the Claude usage block; neither is a menu item.

### Open Project Tab

`OpenProjectMenu` lists every roster project sorted by name. Picking one prompts "Open `<Project>` with which agent?" over every configured provider (Claude, Codex, Gemini, Kimi by default, in that order). This choice is never persisted (see [MCO-§2](docs/BIBLE.md#MCO-§2)). It then opens `ProjectActionMenu` for that project and provider:

| Item | Tag | Description |
|---|---|---|
| Start Editing | `run` | Opens a host tab (`wt … -- MindAttic.Launcher.exe host --name <Project> --provider <Key>`) rooted at the project path, titled `<TabTitle> [<ProviderKey>]`, colored per the project's `TabColor` and `ColorScheme`. |
| Run Command | `runcmd` | Opens a plain `cmd /c <Project.RunCommand>` tab, or a `cmd /k echo …` placeholder tab if none is configured, so the tab does not just flash and close. |
| Settings | `setup` | Opens `ProjectSetupMenu` for this project. |

`ProjectSetupMenu` edits four free-text fields, each a blank-to-clear prompt persisted via `SettingsStore.Update`: Alias (`TabAlias`), Description, Color Scheme (`ColorScheme`) and Tab Color (`TabColor`). There is no Provider row: provider choice is picked fresh every time, not project config.

### Settings menu

`SettingsMenu` edits exactly one thing per provider: the model each agent CLI runs with. For each configured provider it shows `<Name> model` and the model parsed out of its `RunCommand` (or "(CLI default)"). Selecting one opens a picker over `AgentProviderRegistry.KnownModels[key]` (currently populated only for Claude: Fable 5, Opus 4.8, 4.7 and 4.6, Sonnet 5, Sonnet 4.6, Haiku 4.5) plus "Enter model id…" (free text) and "Use CLI default" (clears the flag). `ProviderModel.Set` rewrites the `--model`/`-m` token in place inside `RunCommand` (or appends or removes it), and `AgentProviderRegistry.SetModel` persists the change, materializing the code-level defaults into settings on first edit so there is a row to change. There is no "Default Agent" row and no per-project provider override; the provider is a per-launch choice ([MCO-§2](docs/BIBLE.md#MCO-§2)).

## Settings and persistence

Settings are a single `AppSettings` object, loaded and saved through `SettingsStore` (`MindAttic.Launcher/Services/SettingsStore.cs`) via MindAttic.Vault, at:

```text
%APPDATA%\MindAttic\MindAttic.Launcher\settings.json
```

On first run, if that Vault file does not yet exist, `SettingsStore` looks for a legacy file at `<repo root>\settings.json` (falling back to the historical `D:\Projects\MindAttic\settings.json` when the exe is not running from a checkout) and seeds Vault from it once. A malformed legacy file logs a message to stderr rather than silently producing an empty roster.

`AppSettings` shape (`MindAttic.Launcher/Models/AppSettings.cs`):

| Field | Type | Notes |
|---|---|---|
| `WindowsTerminalSettingsPath` | `string?` | Path to the user's WT `settings.json`, used by `WindowsTerminalSchemes` when splicing a project color scheme. |
| `AgentProviders` | `List<AgentProvider>` | Configured provider list; empty or missing falls back to `AgentProviderRegistry.Defaults` at read time. Nothing is written to disk until a model edit materializes it. |
| `Projects` | `List<Project>` | The roster: every managed repo. |
| `DiscoveryIgnore` | `List<string>?` | Full paths of repos the user chose "never ask again" for during startup discovery. |
| `Extra` | `Dictionary<string, JsonElement>?` | `[JsonExtensionData]`: any top-level key this version does not model (for example a `"mobile"` block a sibling tool writes) round-trips through a save untouched. |

`Project` (`MindAttic.Launcher/Models/Project.cs`) carries `Name`, `Repo`, `RepoUrl` (captured from `origin` at discovery time, not used by launch logic), `Path`, `Description`, `OpenWith` (detected `.slnx`/`.sln` filename), `RunCommand`, `TabAlias`, `TabColor`, `ColorScheme`, `SqlServer` and `Databases` (SQL backup targets), and its own `Extra` bag. It has no provider field.

`AgentProvider` (`MindAttic.Launcher/Models/AgentProvider.cs`) carries `Key`, `Name`, `RunCommand` and `Extra`. Every model with an `Extra` bag round-trips unknown keys losslessly; this is project law [MCO-LAW-2](docs/BIBLE.md#MCO-LAW-2).

## Agent providers and host tabs

`AgentProviderRegistry.Defaults` (`MindAttic.Launcher/Services/AgentProviderRegistry.cs`) ships four built-in providers, used whenever settings has none configured:

| Key | Name | Default RunCommand |
|---|---|---|
| `Claude` | Claude Code | `claude --dangerously-skip-permissions --model claude-sonnet-5-5` |
| `Codex` | OpenAI Codex | `codex --dangerously-bypass-approvals-and-sandbox` |
| `Gemini` | Google Antigravity | `agy --dangerously-skip-permissions` |
| `Kimi` | Kimi Code | `kimi --yolo` |

`AgentProviderRegistry.Current()` (the first-listed provider, Claude by ordering) is what any launch path with no explicit choice resolves to: a bare `host` with no `--provider`, and the Status menu item. Opening a project tab from the menu always prompts instead (see [Open Project Tab](#open-project-tab)); the pick is never saved.

### Credential injection

`Services/ProviderCredentials.cs` runs right before `HostAgentCommand` execs the provider. It resolves a per-provider API key from the shared MindAttic LLM credential keyring (`MindAttic.Vault.Credentials.LlmCredentialStore`, per HOUSE-LAW-3) and pushes it wherever that CLI expects to find it:

- Gemini (`agy`, the Google Antigravity CLI) normally authenticates via its own browser sign-in, but `ProviderCredentials` still sets `GEMINI_API_KEY` on the child process's environment when the keyring has an entry for it; otherwise it is a no-op.
- Kimi only reads its own `~/.kimi-code/config.toml`, so `Services/KimiConfigSync.cs` performs a targeted text splice: it locates `[providers."managed:kimi-code"]` and rewrites just its `api_key = "…"` line, leaving every other table, model and comment untouched (idempotent: a no-op if the value already matches). It refuses to write when that provider already has an `.oauth` sub-table, since Kimi rejects a provider with both `api_key` and `oauth` set.
- Any other provider, or a missing or blank keyring entry, is a no-op: the CLI falls back to however it is already configured. This is never a hard failure.

### Host tabs

Every agent session is a Windows Terminal tab running `MindAttic.Launcher.exe host --name <Project> --provider <Key> --title "<Title> [<Key>]"` (or `--path <dir>` for Status), built by `WindowsTerminalLauncher.BuildAgentTab` / `BuildAgentTabAtPath` and opened with `--tabColor`/`--colorScheme` from the project. Inside that process, two background loops run for the tab's lifetime:

- `TitlePinner` polls the console's bottom rows every 250 ms (`ConsoleBuffer.ReadBottomRows`) for a busy signature ("esc to interrupt", "ctrl+c to cancel", or Claude Code's "N shell(s)" background-job footer) and reasserts the tab title with a busy (`▶`) or idle (`⏸`) glyph whenever the CLI's own title write has clobbered it. Windows Terminal exposes no API for another process to set a tab's title, so the watchdog has to live inside the hosted process. This requires the tab be opened without `--suppressApplicationTitle` (`BuildAgentTab` sets `SuppressApplicationTitle = false`).
- `HostInputPipeServer` listens on a per-tab named pipe (`mindattic-host-{provider}-{pid}`, single-instance) and injects any text it receives into the console's input buffer via `ConsoleInputInjector`, the delivery mechanism for [Remote control](#remote-control).

## Remote control

Driving an agent tab from a phone or tablet is handled by Claude Code's own built-in `/remote-control`, typed inside the tab like any other slash command. The previous MindAttic.Mobile SignalR bridge has been removed from the workspace.

Separately, `Services/RemoteControlBroadcaster.cs` implements a pipe-based fan-out: given a provider key and a payload, it enumerates `\\.\pipe\` for every live `mindattic-host-{provider}-*` pipe (one per open host tab for that provider) and writes the payload to each in parallel, so a single call could type `/remote-control` (or anything else) into every open Claude tab at once. The class is implemented and unit-tested (`RemoteControlBroadcasterTests`: pipe-prefix filtering, zero-match reporting) but is not currently wired into a menu item or CLI sub-command. Treat it as a tested building block awaiting a front door.

## Backup

The Backup menu item runs `BackupMenu.Run()` directly (no submenu). It:

1. Resolves a collision-safe target folder via `BackupService.ResolveTargetFolder()`: `R:\Backup\MindAttic\<yyyy-MM-dd>`, or the first free `<date>_a` through `<date>_z` if today's plain folder is taken (it throws only if all 27 slots are used).
2. Collects SQL backup targets via `SqlBackupService.CollectTargets(settings)`: every non-blank name in each project's `Databases`, paired with that project's `SqlServer` (or `SqlBackupService.DefaultInstance`, `"localhost"`), deduplicated case-insensitively per server and database pair.
3. Shows the source, target and database list and asks for confirmation.
4. Runs the file backup: `robocopy <source> <target> /E …` excluding `Library, Temp, Logs, obj, bin, Build, Builds, node_modules, .vs, .idea, .git` and `*.log, *.tmp`, with a live byte-count status tick. Exit codes 0 to 7 are success; 8 or above (or a cancellation) is failure.
5. Runs the database backups (unconditionally, even if the file backup failed) via `SqlBackupService.Backup`: each target gets `sqlcmd -S <server> -E -C -b -Q "BACKUP DATABASE … WITH FORMAT, INIT, COPY_ONLY, CHECKSUM, NAME = N'MindAttic.Launcher backup';"`, written to `<target>\Databases\<server>\<database>.bak` (both path segments sanitized of illegal filename characters). A failed or cancelled backup deletes its own half-written `.bak` rather than leaving a corrupt file behind.
6. Reports both outcomes: byte totals and elapsed time for the file copy, and per-database OK or FAILED with `sqlcmd` exit codes and a truncated error tail.

This exists because a `robocopy` snapshot of a live `.mdf` file is not a real database backup ([MCO-LAW-3](docs/BIBLE.md#MCO-LAW-3)).

## Windows Terminal color schemes

Every project tab gets a `--tabColor` (a plain hex the tab strip renders) and optionally a matching `MindAttic-<Name>` scheme in Windows Terminal's own `settings.json`: all ANSI colors shared, only `background` differs, derived by scaling the tab color to about 16% brightness (`ColorPalette.DarkBackground`). `Services/WindowsTerminalSchemes.cs` writes this scheme with a targeted text splice (locate the `schemes` array, insert the block after its `[`) rather than a JSON parse and reserialize, so the user's hand-maintained WT settings file is not reflowed. The splice is idempotent: re-running it with a scheme name that already exists returns the file unchanged ([MCO-LAW-4](docs/BIBLE.md#MCO-LAW-4)).

### Project discovery

`Services/ProjectDiscovery.cs` scans the immediate subdirectories of the resolved workspace root for git repos (a `.git` directory or, for worktrees, a `.git` file) not already in `Projects` or `DiscoveryIgnore`, and surfaces them at startup via `DiscoverProjectsMenu`. For each candidate it prompts for the git URL (pre-filled from `origin` if detectable), a tab color from `ColorPalette.Colors` (a curated 16-entry palette) or a typed custom hex, and an optional description, then appends the project to the roster and writes its WT scheme. Press `S` to skip all remaining candidates this run, `N` to never ask about this one again (added to `DiscoveryIgnore`), or `Esc` to skip just this one.

## Deploys

The Launcher has no deploy menu item or sub-command; run MindAttic.Deploy directly (`MindAttic.Deploy.exe all`, or its interactive menu). This repo itself has no web deploy: this GitHub README is the project page. This binary owns no FTP pipeline and no per-project deploy state ([MCO-LAW-5](docs/BIBLE.md#MCO-LAW-5)); deploying is, and will remain, MindAttic.Deploy's job.

## Limitations

MindAttic.Launcher is the orchestrator, not the thing being orchestrated. It is not:

- An agent. It execs a provider CLI with inherited stdio (`host`); no code path here links an LLM SDK or makes an LLM API call.
- A phone or tablet web terminal. That was MindAttic.Mobile (a WebSocket and xterm.js bridge), now removed from the workspace. Remote driving of a tab is Claude Code's own `/remote-control`.
- A deploy engine. Deploys are MindAttic.Deploy's job (`MindAttic.Deploy.exe all`); this repo's `/deploy` slash command only explains that it has no web deploy.
- A general settings UI. It edits only its own roster and provider settings and the Windows Terminal `schemes` array.
- Cross-platform. It targets `net10.0-windows` / `win-x64` and depends on Windows Terminal (`wt`), `robocopy` and `sqlcmd` being on `PATH`.
- A credential store. API keys are resolved through MindAttic.Vault's shared keyring (`MindAttic.Vault.Credentials.LlmCredentialStore`); this repo never hard-codes a secret.

The backup target (`R:\Backup\MindAttic`) and the legacy settings fallback path are fixed in code (`BackupService.DefaultBackupBase`, `SettingsStore`).

## Building and testing

```powershell
# Build (TreatWarningsAsErrors=true, Nullable=enable, LangVersion=latest)
dotnet build

# Test: NUnit 4, run from the repo root
dotnet test

# Publish a single-file, framework-dependent win-x64 exe to artifacts\
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1
#   -Clean   : wipe bin/obj first for a full, timestamp-independent rebuild
```

`scripts\ensure-fresh.ps1` republishes `artifacts\MindAttic.Launcher.exe` only when it is missing or a source file is newer than it (a fast no-op otherwise); `-Force` skips that heuristic. It is called on every launch (by whatever wrapper invokes the exe) and by the in-app Restart flow. `scripts\restart.ps1` is what Restart launches in a fresh tab: it waits for the old process's PID to exit (releasing the Windows lock on the running exe image), force-republishes, then hands the tab to the fresh binary, refusing to fall back to a stale exe on a failed rebuild.

Test coverage (`MindAttic.Launcher.Tests/`, NUnit 4) spans:

- settings and Vault round-trip, legacy-seed migration and unknown-key preservation
- agent-provider list resolution, default ordering and `--model` token rewriting
- `ProviderCredentials` / `KimiConfigSync` credential injection (environment variable and config.toml splice, oauth guard, idempotency)
- `CommandLineToArgvW`-style argv quoting
- `git --porcelain` parsing (renames, both-modified, untracked, quoted paths) and auto commit-message composition and truncation
- the dated backup-folder allocator and exclude lists, and SQL backup path, SQL and argument composition
- Windows Terminal scheme-splice idempotency and launcher tab-building
- project discovery and roster sorting, and tab-title alias and prefix rules
- title-pinner busy detection (including the background-shell footer), remote-control pipe broadcast filtering, and build-freshness day-floor and timezone comparison

After editing anything under `docs/`, run `powershell -File tools/codex.ps1 doctor` (validates IDs, links, front-matter and cited tests; `codex.ps1 digest` regenerates `docs/BIBLE.digest.md`).

## Project layout

```text
MindAttic.Launcher/                  the exe project
  Commands/                          Spectre.Console.Cli sub-commands (host, commit, version, main menu)
  Menus/                             interactive Spectre.Console screens
  Services/                          logic + external-process/filesystem seams (git, wt, robocopy, sqlcmd, Vault, ...)
  Models/                            persisted nouns (Project, AgentProvider, AppSettings)
  Ui/                                Menu/Screen/Theme: the keyboard-driven prompt widget + chrome
  Interop/                           CommandLineParser, ConsoleBuffer, ConsoleInputInjector (Win32-adjacent helpers)
  M.ico                              exe icon
MindAttic.Launcher.Tests/            NUnit 4 test project (roughly one *Tests.cs per service/model)
docs/
  BIBLE.md                           L0: architecture, Laws, verified state, glossary
  AMENDMENTS.md                      L1: pending decisions not yet folded into the bible (normally empty)
  USER_STORIES.md                    L2: test-cited stories
  BIBLE.digest.md                    GENERATED, never hand-edit
  rfc/                               open design notes (deleted once decided and folded in)
scripts/
  publish.ps1                        dotnet publish -> artifacts\MindAttic.Launcher.exe
  ensure-fresh.ps1                   conditional republish (staleness heuristic)
  restart.ps1                        used by the in-app Restart flow (waits out the exe lock)
tools/
  codex.ps1                          docs doctor/digest tooling
  build-readme.ps1                   thin wrapper -> workspace-shared README->HTML engine
artifacts/                           publish output (gitignored)
Directory.Build.props                shared MSBuild settings (Nullable, TreatWarningsAsErrors, ...)
MindAttic.Launcher.slnx              solution file
NuGet.config, global.json            package source + SDK roll-forward
```

## Glossary

| Term | Meaning |
|---|---|
| Workspace root | `D:\Projects\MindAttic`, the parent directory holding every MindAttic repo. |
| Roster | The `Projects` list in settings; the set of managed repos. |
| Provider | A launchable agent CLI (`AgentProvider.RunCommand`), for example Claude, Codex, Gemini, Kimi. |
| Host tab | A `wt` tab running `MindAttic.Launcher.exe host`, which execs a provider with inherited stdio, rooted at a repo (or a `--path` directory, for example the Status tab). |
| Pinner | `TitlePinner`, the per-tab background loop that keeps a busy or idle glyph in the tab title. |
| Discovery | The startup scan for git repos under the workspace root that are not yet in the roster. |
| Vault | MindAttic.Vault, the shared `%APPDATA%\MindAttic\…` settings and secret store this repo persists through. |
| Scheme | A `MindAttic-<Name>` Windows Terminal color scheme: one shared ANSI palette, a per-project background. |
| Sibling repo | Another repo under the workspace root (for example MindAttic.Deploy, MindAttic.Vault). |

## Documentation

- [docs/BIBLE.md](docs/BIBLE.md) (L0): architecture canon, invariants and the Laws.
- [docs/AMENDMENTS.md](docs/AMENDMENTS.md) (L1): pending decisions not yet folded into the bible (normally empty).
- [docs/USER_STORIES.md](docs/USER_STORIES.md) (L2): per-capability status, each done story citing the test that verifies it.
- [docs/BIBLE.digest.md](docs/BIBLE.digest.md): generated digest of the bible.
- [AGENTS.md](AGENTS.md) and [CLAUDE.md](CLAUDE.md): agent instructions and the documentation-layering rules.

## License

This repository has no license file; all rights reserved.

---

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic). Related: [MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault) (settings and credential store) and [MindAttic.Deploy](https://github.com/mindattic/MindAttic.Deploy) (deploy engine).
