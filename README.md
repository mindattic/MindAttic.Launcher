# MindAttic.Terminal

C# CLI for MindAttic orchestration. Single binary, `MindAttic.Terminal.exe`,
that handles project menus, per-tab agent hosting, commit/push, and backup.
Tab spawning goes through Windows Terminal (`wt`); the agent host that runs
inside each tab is `mindattic host`.

## Build & publish

From the repo root:

```pwsh
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1
```

Output: `artifacts\MindAttic.Terminal.exe` — single-file, framework-dependent, `win-x64`
(~1.5 MB). The `MindAttic.Terminal.bat` shim at `D:\Projects\MindAttic\`
publishes on first run automatically.

## Usage

| Form | Behavior |
| --- | --- |
| `MindAttic.Terminal` | Interactive Spectre.Console menu (Commit, Open Project Tab, Run Project, Backup, Provider, Restart). |
| `MindAttic.Terminal host --name <Project> [--provider <Key>] [--title <Tab>]` | Per-tab agent host: sets the tab title, starts the title pinner, and execs the configured agent provider with inherited stdio. |
| `MindAttic.Terminal commit [--project <Project>] [--message "..."]` | Commit + push one or all projects. Auto-generates the message from `git status --porcelain` when none is supplied. |
| `MindAttic.Terminal.bat` | Launches `MindAttic.Terminal` and triggers a first-time publish if the exe is missing. |
| `MindAttic.Terminal.bat --tab Foo` | Quick-spawn a `wt` tab running `MindAttic.Terminal host --name Foo`. |

## Settings

Persisted via [MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault) at:

```
%APPDATA%\MindAttic\MindAttic.Terminal\settings.json
```

On first run, if the Vault settings file is missing AND
`D:\Projects\MindAttic\settings.json` exists, the legacy file is read once to
seed Vault.

## Remote control

Driving an agent tab from a phone or iPad is now handled by Claude Code's
built-in `/remote-control`. The previous MindAttic.Mobile SignalR bridge
(`Services/MobileBridge.cs` and friends) has been removed; nothing in this
launcher needs to know about it.

## Tests

NUnit 4. Run from the repo root:

```pwsh
dotnet test
```

Coverage spans settings/vault round-trips, the legacy-seed migration,
`CommandLineToArgvW` quoting edge cases, agent-provider resolution + cycling,
git `--porcelain` parsing (incl. renames, MM both-modified, untracked, quoted
paths), the auto commit-message + 200-char summary fallback, and the backup
dated-folder allocator.

