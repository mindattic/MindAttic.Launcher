# MindAttic.Terminal

C# CLI for MindAttic orchestration. Replaces the PowerShell-based MindAttic
project launcher (`MindAttic.ps1` + `console-launcher.ps1`) with a single
binary, `MindAttic.Terminal.exe`. Tab spawning still goes through Windows Terminal
(`wt`); the agent host that runs inside each tab is `mindattic host`.

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
| `MindAttic.Terminal host --name <Project> [--provider <Key>] [--title <Tab>]` | Per-tab agent host. Replaces `console-launcher.ps1`. |
| `MindAttic.Terminal commit [--project <Project>] [--message "..."]` | Commit + push one or all projects. Auto-generates the message from `git status --porcelain` when none is supplied. |
| `MindAttic.Terminal.bat` | Launches `MindAttic.Terminal` and triggers a first-time publish if the exe is missing. |
| `MindAttic.Terminal.bat --tab Foo` | Quick-spawn a `wt` tab running `MindAttic.Terminal host --name Foo`. |

## Settings

Persisted via [MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault) at:

```
%APPDATA%\MindAttic\MindAttic.Terminal\settings.json
```

`Mobile.Token` is held in a separate Vault bucket and never written to
`settings.json`:

```
%APPDATA%\MindAttic\MindAttic.Mobile\tokens.json
```

On first run, if the Vault settings file is missing AND
`D:\Projects\MindAttic\settings.json` exists, the legacy file is read once to
seed Vault — including moving any plaintext `Mobile.Token` into the token
bucket.

## Mobile bridge

The handoff to `MindAttic.Mobile.AgentHost.exe` (SignalR proxy that lets a
phone/iPad drive a tab) is fully implemented in `Services/MobileBridge.cs`
but **gated behind a kill switch** while `MindAttic.Mobile` is still pre-ship:

```csharp
public static readonly bool FeatureEnabled = false;
```

Flip to `true` once Mobile is ready. The paired test
`MobileBridgeTests.FeatureEnabled_is_currently_off` will start failing —
update it together with the flip.

## Tests

NUnit 4. Run from the repo root:

```pwsh
dotnet test
```

Coverage spans settings/vault round-trips, the legacy-seed migration,
`CommandLineToArgvW` quoting edge cases, agent-provider resolution + cycling,
git `--porcelain` parsing (incl. renames, MM both-modified, untracked, quoted
paths), the auto commit-message + 200-char summary fallback, the backup
dated-folder allocator, and the Mobile bridge gate logic (currently dormant
behind the kill switch).

## Retiring the PowerShell launcher

`D:\Projects\MindAttic\MindAttic.ps1` and `console-launcher.ps1` stay in
place as a fallback. Remove them once the .NET launcher has been stable for
~a week.
