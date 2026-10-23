using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MindAttic.Launcher.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MindAttic.Launcher.Commands;

/// <summary>
/// New-machine setup: install prerequisites and the AI agent CLIs, restore AppData, clone (or
/// pull) every <c>wip</c>-tagged repo, start LocalDB, restore the databases. Launcher's answer to
/// Bob's <c>--unpack</c> — except this is recovery onto a machine that already has git, the .NET
/// runtime, and this very exe (the one piece that can't be Launcher code; see <c>setup.ps1</c> at
/// the repo root for that seed step). No self-elevation: <c>winget</c> raises its own UAC prompt
/// only for the one package that needs it.
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

        [CommandOption("--server <SERVER>")]
        [Description(@"SQL Server instance to restore every .bak into (default: (localdb)\MSSQLLocalDB, which bootstrap installs and starts).")]
        public string? Server { get; init; }

        [CommandOption("--skip-optional")]
        [Description("Don't prompt for optional dev tools (VS Code, Visual Studio, SSMS, Notepad++, Node, Claude desktop, Antigravity).")]
        public bool SkipOptional { get; init; }
    }

    /// <summary>
    /// Bootstrap's phases, in the order <see cref="RunPhases"/> runs them. AppData comes before the
    /// repos because the GitHub token that finds the <c>wip</c>-tagged repos lives in AppData
    /// (<c>%APPDATA%\MindAttic\GitHub\tokens.json</c>), and so does the roster the SQLite restore
    /// matches dumps against; the databases come after the repos, whose folders the SQLite files
    /// live in, and after LocalDB is running.
    /// </summary>
    public sealed record Phases(
        Func<bool> Prerequisites,
        Func<bool> OptionalTools,
        Func<bool> AiClis,
        Func<bool> RestoreAppData,
        Func<bool> Repos,
        Func<bool> StartLocalDb,
        Func<bool> RestoreDatabases);

    /// <summary>Runs every phase in order — one failing never skips the rest — and reports whether all succeeded.</summary>
    public static bool RunPhases(Phases phases)
    {
        var ok = true;
        foreach (var phase in new[]
                 {
                     phases.Prerequisites, phases.OptionalTools, phases.AiClis, phases.RestoreAppData,
                     phases.Repos, phases.StartLocalDb, phases.RestoreDatabases
                 })
            ok &= phase();
        return ok;
    }

    /// <summary>
    /// The outside world <see cref="Run"/> touches, so tests can swap each piece. The defaults are
    /// the real services.
    /// </summary>
    public sealed record Seams(
        PrerequisiteService Prerequisites,
        WorkspaceCloneService Cloner,
        Func<string, bool> Confirm,
        Func<string?> GetToken,
        Func<IReadOnlySet<string>?> FetchTagged,
        Func<string, bool> RestoreAppData,
        Func<string, string, bool> RestoreSqlDatabases,
        Func<string, bool> RestoreSqliteDatabases,
        string BackupBase)
    {
        public static Seams Real() => new(
            new PrerequisiteService(),
            new WorkspaceCloneService(),
            question => AnsiConsole.Confirm(question, defaultValue: false),
            () => GitHubCredentials.GetToken(),
            () => TopicRepoSync.FetchOrNull(),
            RestoreCommand.RestoreAppData,
            RestoreCommand.RestoreSqlDatabases,
            RestoreCommand.RestoreSqliteDatabases,
            BackupService.DefaultBackupBase);
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var workspaceRoot = settings.WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            workspaceRoot = AnsiConsole.Ask("Workspace root?", @"D:\Projects\MindAttic");
        return Run(workspaceRoot, settings, Seams.Real());
    }

    /// <summary>The whole pipeline against <paramref name="seams"/>; returns the process exit code.</summary>
    public static int Run(string workspaceRoot, Settings settings, Seams seams)
    {
        Directory.CreateDirectory(workspaceRoot);

        var server = string.IsNullOrWhiteSpace(settings.Server) ? PrerequisiteService.DefaultLocalDbServer : settings.Server.Trim();
        var prereq = seams.Prerequisites;
        var from = settings.From ?? FindNewestBackupFolder(seams.BackupBase);

        var ok = RunPhases(new Phases(
            Prerequisites: () => EnsurePrerequisites(prereq),
            OptionalTools: () => { if (!settings.SkipOptional) OfferOptionalTools(prereq, seams.Confirm); return true; },
            AiClis: () => { EnsureAiClis(prereq); return true; },
            RestoreAppData: () => RestoreAppData(from, seams.RestoreAppData),
            Repos: () => CloneRepos(workspaceRoot, seams.GetToken, seams.FetchTagged, seams.Cloner),
            StartLocalDb: () => StartLocalDb(prereq, server),
            RestoreDatabases: () => RestoreDatabases(from, server, seams.RestoreSqlDatabases, seams.RestoreSqliteDatabases)));

        return ok ? 0 : 1;
    }

    internal static bool EnsurePrerequisites(PrerequisiteService prereq)
    {
        AnsiConsole.MarkupLine("[bold]Prerequisites[/]");
        var ok = true;
        foreach (var (command, wingetIds) in PrerequisiteService.HardRequirements)
        {
            var r = prereq.EnsureHard(command, wingetIds);
            ok &= r.Present;
            AnsiConsole.MarkupLine(r.Present
                ? $"  [green]OK[/] {Markup.Escape(command)}{(r.Installed ? " (installed)" : "")}"
                : $"  [red]MISSING[/] {Markup.Escape(command)} — {Markup.Escape(r.Error ?? "")}");
        }
        return ok;
    }

    internal static void OfferOptionalTools(PrerequisiteService prereq, Func<string, bool> confirm)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Optional tools[/]");
        foreach (var tool in PrerequisiteService.OptionalTools)
        {
            if (prereq.IsInstalled(tool))
            {
                AnsiConsole.MarkupLine($"  [grey50]{Markup.Escape(tool.Name)} already installed — skipped[/]");
                continue;
            }
            if (!confirm($"  Install {tool.Name}?")) continue;

            var (installOk, output) = prereq.InstallViaWinget(tool.WingetId);
            AnsiConsole.MarkupLine(installOk
                ? $"  [green]Installed {Markup.Escape(tool.Name)}[/]"
                : $"  [yellow]Could not install {Markup.Escape(tool.Name)}: {Markup.Escape(output)}[/]");
        }
    }

    /// <summary>The CLIs the tabs run. Never fails bootstrap: the pipeline itself doesn't need them.</summary>
    private static void EnsureAiClis(PrerequisiteService prereq)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]AI agent CLIs[/]");
        foreach (var cli in PrerequisiteService.AiClis)
        {
            var r = prereq.EnsureAiCli(cli);
            AnsiConsole.MarkupLine(r.Present
                ? $"  [green]OK[/] {Markup.Escape(cli.Name)}{(r.Installed ? " (installed)" : "")}"
                : $"  [yellow]Not installed[/] {Markup.Escape(cli.Name)} — {Markup.Escape(r.Error ?? "")}");
        }
    }

    private static bool RestoreAppData(string? from, Func<string, bool> restore)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]AppData[/]");
        if (from is null)
        {
            AnsiConsole.MarkupLine($"  [grey50]No backup folder reachable under {Markup.Escape(BackupService.DefaultBackupBase)} — starting empty.[/]");
            return true;
        }
        if (!Directory.Exists(from))
        {
            AnsiConsole.MarkupLine($"  [red]--from folder not found: {Markup.Escape(from)}[/]");
            return false;
        }
        AnsiConsole.MarkupLine($"  Restoring from {Markup.Escape(from)}");
        return restore(from);
    }

    internal static bool CloneRepos(string workspaceRoot, Func<string?> getToken, Func<IReadOnlySet<string>?> fetchTagged, WorkspaceCloneService cloner)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Repos[/]");
        var token = getToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            AnsiConsole.MarkupLine("  [yellow]No GitHub token configured (none in the restored AppData either) — can't discover which repos to clone.[/]");
            AnsiConsole.MarkupLine("  [grey50]Set one from the Settings menu once Launcher is up, then run 'bootstrap' again.[/]");
            return false;
        }

        var tagged = fetchTagged();
        if (tagged is null)
        {
            AnsiConsole.MarkupLine("  [yellow]Could not fetch the tagged repo list from GitHub.[/]");
            return false;
        }

        var results = cloner.CloneAll(workspaceRoot, tagged, r =>
        {
            AnsiConsole.MarkupLine(r.Ok
                ? $"  [green]{(r.AlreadyPresent ? "pulled" : "cloned")}[/] {Markup.Escape(r.RepoName)}{(r.AlreadyPresent && r.Message is not null ? $" [grey50]({Markup.Escape(r.Message)})[/]" : "")}"
                : $"  [red]FAILED[/] {Markup.Escape(r.RepoName)}: {Markup.Escape(r.Message ?? "")}");
        });
        return results.All(r => r.Ok);
    }

    /// <summary>A fresh LocalDB install has no running instance; start (or create) the one restores target.</summary>
    private static bool StartLocalDb(PrerequisiteService prereq, string server)
    {
        var instance = PrerequisiteService.LocalDbInstanceOf(server);
        if (instance is null) return true;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]LocalDB[/]");
        var (ok, message) = prereq.EnsureLocalDbRunning(instance);
        AnsiConsole.MarkupLine(ok
            ? $"  [green]OK[/] {Markup.Escape(server)} {Markup.Escape(message)}"
            : $"  [red]FAILED[/] {Markup.Escape(server)}: {Markup.Escape(message)}");
        return ok;
    }

    private static bool RestoreDatabases(string? from, string server, Func<string, string, bool> restoreSql, Func<string, bool> restoreSqlite)
    {
        if (from is null || !Directory.Exists(from)) return true; // already reported by the AppData phase

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Databases[/] [grey50](into {Markup.Escape(server)})[/]");
        var ok = restoreSql(from, server);
        ok &= restoreSqlite(from);
        return ok;
    }

    internal static string? FindNewestBackupFolder(string backupBase)
    {
        try
        {
            if (!Directory.Exists(backupBase)) return null;
            return Directory.GetDirectories(backupBase)
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
