# MindAttic.Launcher

C# CLI for MindAttic workspace orchestration. Single binary (`MindAttic.Launcher.exe`), `net10.0-windows`, `win-x64`.

Provides an interactive Spectre.Console menu for the daily driver — commit, pull, open a project tab, backup, settings — plus `host` / `commit` / `version` subcommands for automated and per-tab use.

## Build & publish

From the repo root:

```pwsh
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1
```

Output: `artifacts\MindAttic.Launcher.exe` — single-file, framework-dependent, `win-x64`.

## Usage

| Form | Behavior |
|---|---|
| `MindAttic.Launcher` | Interactive Spectre.Console menu (Commit, Pull, Open Project Tab, Backup, Settings, Status, Open Command Prompt, Open PowerShell, Restart, Exit). |
| `MindAttic.Launcher host --name <Project> [--provider <Key>] [--title <Tab>] [--prompt <Text>]` | Per-tab agent host: sets the tab title, starts the title pinner, and execs the configured agent provider with inherited stdio. |
| `MindAttic.Launcher host --path <Dir>` | Root the agent at an arbitrary directory (Overlord uses the MindAttic workspace root). |
| `MindAttic.Launcher commit [--project <Name>] [--message "..."]` | Commit + push one or all projects. Auto-generates the message from `git status --porcelain` when none is supplied. |
| `MindAttic.Launcher version` | Print version and exe path. |

## Menu items

| Item | Description |
|---|---|
| Commit and sync | Commit and push changes per project or across all |
| Pull | `git pull --ff-only` per project or across all |
| Open Project Tab | Open a project in a new Windows Terminal tab with its configured coding agent |
| Backup | Back up MindAttic to `R:\Backup\MindAttic` |
| Settings | Default agent, model per agent, per-project overrides |
| Status | Open a Claude tab at the workspace root with `/status` pre-filled |
| Open Command Prompt (Admin) | Open cmd as Administrator at the workspace root |
| Open PowerShell (Admin) | Open PowerShell as Administrator at the workspace root |
| Restart | Reload this console in a new tab |
| Exit | Close the menu |

The **Overlord** option (available inside Open Project Tab) opens a single agent session rooted at the MindAttic workspace root, letting one agent address any repo under it.

## Settings

Persisted via [MindAttic.Vault](https://mindattic.com/mindatticvault.htm) at:

```
%APPDATA%\MindAttic\MindAttic.Launcher\settings.json
```

On first run, if the Vault settings file is missing and `D:\Projects\MindAttic\settings.json` exists, the legacy file is read once to seed Vault.

## Remote control

Driving an agent tab from a phone or iPad is handled by Claude Code's built-in `/remote-control`. The previous `MindAttic.Mobile` SignalR bridge has been removed.

## Tests

NUnit 4. Run from the repo root:

```pwsh
dotnet test
```

Coverage includes: settings/Vault round-trips, legacy-seed migration, `CommandLineToArgvW` quoting edge cases, agent-provider resolution and cycling, git `--porcelain` parsing (renames, MM both-modified, untracked, quoted paths), auto commit-message and 200-char summary fallback, backup dated-folder allocator, Windows Terminal scheme generation, SQL backup service, deploy service, build freshness, and remote-control broadcaster.
