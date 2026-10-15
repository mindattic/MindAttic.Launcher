using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Services;
using MindAttic.Vault.Credentials;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

/// <summary>
/// New-machine setup: install prerequisites, clone every <c>wip</c>-tagged repo, restore the
/// latest reachable backup. Launcher's answer to Bob's <c>--unpack</c> — except this is recovery
/// onto a machine that already has git, the .NET runtime, and this very exe (the one piece that
/// can't be Launcher code; see <c>bootstrap.ps1</c> at the repo root for that seed step). No
/// self-elevation: <c>winget</c> raises its own UAC prompt only for the one package that needs it.
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Spectre.Console.Cli")]
public sealed class BootstrapCommand : Command<BootstrapCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--workspace-root <PATH>")]
        [Description(@"Workspace root to clone repos into (default: D:\Projects\MindAttic, prompted if not passed).")]
        public string? WorkspaceRoot { get; init; }

        [CommandOption("--from <FOLDER>")]
        [Description(@"Dated backup folder to restore from (default: the newest one reachable under R:\Backup\MindAttic).")]
        public string? From { get; init; }

        [CommandOption("--skip-optional")]
        [Description("Don't prompt for optional dev tools (VS Code, Visual Studio, SSMS, Notepad++, Node, Claude desktop, Antigravity).")]
        public bool SkipOptional { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var workspaceRoot = settings.WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            workspaceRoot = AnsiConsole.Ask("Workspace root?", @"D:\Projects\MindAttic");
        Directory.CreateDirectory(workspaceRoot);

        var ok = true;

        AnsiConsole.MarkupLine("[bold]Prerequisites[/]");
        var prereq = new PrerequisiteService();
        foreach (var (command, wingetIds) in PrerequisiteService.HardRequirements)
        {
            var r = prereq.EnsureHard(command, wingetIds);
            ok &= r.Present;
            AnsiConsole.MarkupLine(r.Present
                ? $"  [green]OK[/] {Markup.Escape(command)}{(r.Installed ? " (installed)" : "")}"
                : $"  [red]MISSING[/] {Markup.Escape(command)} — {Markup.Escape(r.Error ?? "")}");
        }

        if (!settings.SkipOptional)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Optional tools[/]");
            foreach (var tool in PrerequisiteService.OptionalTools)
            {
                if (prereq.CommandExists(tool.CommandOnPath))
                {
                    AnsiConsole.MarkupLine($"  [grey50]{Markup.Escape(tool.Name)} already installed — skipped[/]");
                    continue;
                }
                if (!AnsiConsole.Confirm($"  Install {tool.Name}?", defaultValue: false)) continue;

                var (installOk, output) = prereq.InstallViaWinget(tool.WingetId);
                AnsiConsole.MarkupLine(installOk
                    ? $"  [green]Installed {Markup.Escape(tool.Name)}[/]"
                    : $"  [yellow]Could not install {Markup.Escape(tool.Name)}: {Markup.Escape(output)}[/]");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Repos[/]");
        var token = GitHubCredentials.DefaultStore.Get(GitHubCredentials.TokenName);
        if (string.IsNullOrWhiteSpace(token))
        {
            AnsiConsole.MarkupLine("  [yellow]No GitHub token configured — can't discover which repos to clone.[/]");
            AnsiConsole.MarkupLine("  [grey50]Set one from the Settings menu once Launcher is up, then run 'bootstrap' again.[/]");
            ok = false;
        }
        else
        {
            var tagged = TopicRepoSync.FetchOrNull();
            if (tagged is null)
            {
                AnsiConsole.MarkupLine("  [yellow]Could not fetch the tagged repo list from GitHub.[/]");
                ok = false;
            }
            else
            {
                var clone = new WorkspaceCloneService();
                var results = clone.CloneAll(workspaceRoot, tagged, r =>
                {
                    AnsiConsole.MarkupLine(r.Ok
                        ? $"  [green]{(r.AlreadyPresent ? "present" : "cloned")}[/] {Markup.Escape(r.RepoName)}"
                        : $"  [red]FAILED[/] {Markup.Escape(r.RepoName)}: {Markup.Escape(r.Message ?? "")}");
                });
                ok &= results.All(r => r.Ok);
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Restore[/]");
        var from = settings.From ?? FindNewestBackupFolder();
        if (from is null)
        {
            AnsiConsole.MarkupLine($"  [grey50]No backup folder reachable under {Markup.Escape(BackupService.DefaultBackupBase)} — starting empty.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  Restoring from {Markup.Escape(from)}");
            var restoreExit = new RestoreCommand().Execute(context, new RestoreCommand.Settings { From = from });
            ok &= restoreExit == 0;
        }

        return ok ? 0 : 1;
    }

    private static string? FindNewestBackupFolder()
    {
        try
        {
            if (!Directory.Exists(BackupService.DefaultBackupBase)) return null;
            return Directory.GetDirectories(BackupService.DefaultBackupBase)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            // Unreachable network path, permissions, etc. — bootstrap continues with an empty start.
            return null;
        }
    }
}
