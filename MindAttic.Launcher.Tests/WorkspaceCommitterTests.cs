using System.Diagnostics;
using MindAttic.Launcher.Models;
using MindAttic.Launcher.Services;
using MindAttic.Workflow;
using MindAttic.Workflow.Data;
using MindAttic.Workflow.Tickets;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class WorkspaceCommitterTests
{
    private string root = "";
    private string dbPath = "";

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "mco-commit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        dbPath = Path.Combine(root, WorkspaceCommitter.WorkflowRepoName, "data", "workflow.db");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }

    [Test]
    public void TicketIds_finds_plain_and_dotted_codes()
    {
        Assert.That(WorkspaceCommitter.TicketIds("Fix backup (MCO-T21), see M.LNCH-T5 and MCO-T21"),
            Is.EqualTo(new[] { "MCO-T21", "M.LNCH-T5" }));
        Assert.That(WorkspaceCommitter.TicketIds("Update README"), Is.Empty);
    }

    [Test]
    public void InferType_reads_intent_from_the_message_and_docs_from_the_files()
    {
        GitChange[] code = [new(ChangeKind.Modified, "Foo.cs")];
        GitChange[] docs = [new(ChangeKind.Modified, "README.md"), new(ChangeKind.Added, "guide.htm")];

        Assert.That(WorkspaceCommitter.InferType("Fix the restore path", false, code), Is.EqualTo("fix"));
        Assert.That(WorkspaceCommitter.InferType("Add SQLite backups", false, code), Is.EqualTo("feature"));
        Assert.That(WorkspaceCommitter.InferType("Rename settings keys", false, code), Is.EqualTo("chore"));
        Assert.That(WorkspaceCommitter.InferType("Add Foo.cs", true, code), Is.EqualTo("chore"), "an auto message names files, not intent");
        Assert.That(WorkspaceCommitter.InferType("Fix typos", false, docs), Is.EqualTo("docs"));
    }

    [Test]
    public void WithTicket_appends_the_id_to_the_subject_line()
    {
        Assert.That(WorkspaceCommitter.WithTicket("Fix restore", "MCO-T9"), Is.EqualTo("Fix restore (MCO-T9)"));
        Assert.That(WorkspaceCommitter.WithTicket("Fix restore\n\nbody", "MCO-T9"), Is.EqualTo("Fix restore (MCO-T9)\n\nbody"));
    }

    [Test]
    public void Commit_without_a_ticket_creates_a_closed_one_and_commits_its_view_with_the_change()
    {
        var alpha = NewRepo("Alpha");
        NewRepo(WorkspaceCommitter.WorkflowRepoName);
        Register(alpha, "ALP");
        File.WriteAllText(Path.Combine(alpha, "a.cs"), "class A {}");

        var results = Committer().Run([Project("Alpha", alpha)], "Add the A class");

        var alphaResult = results.Single(r => r.Project == "Alpha");
        Assert.That(alphaResult.TicketCreated, Is.True);
        Assert.That(alphaResult.TicketId, Is.EqualTo("ALP-T1"));
        Assert.That(alphaResult.Detail, Does.StartWith(GitService.PushFailedPrefix), "no remote in the test, but the commit landed");
        Assert.That(Git(alpha, "log", "-1", "--format=%s"), Is.EqualTo("Add the A class (ALP-T1)"));
        Assert.That(Git(alpha, "status", "--porcelain"), Is.Empty, "the rewritten USER_STORIES.md view went into the same commit");
        Assert.That(File.ReadAllText(Path.Combine(alpha, "docs", "USER_STORIES.md")), Does.Contain("ALP-T1"));

        using (var db = WorkflowDb.Open(dbPath))
        {
            var t = db.Ticket("ALP-T1")!.Value.Ticket;
            Assert.That(t.Status, Is.EqualTo(TicketRules.Closed));
            Assert.That(t.Type, Is.EqualTo("feature"));
            Assert.That(t.Summary, Is.EqualTo("Add the A class"));
        }

        var sync = results.Single(r => r.Project == WorkspaceCommitter.WorkflowRepoName);
        Assert.That(sync.CommitMessage, Is.EqualTo(WorkspaceCommitter.SyncMessageFor("ALP-T1")), "the database itself is committed after a single-repo run, citing the ticket it carries");
        Assert.That(Git(Path.Combine(root, WorkspaceCommitter.WorkflowRepoName), "log", "-1", "--format=%s"), Is.EqualTo("Sync the workflow database (ALP-T1)"));
    }

    [Test]
    public void Commit_naming_an_existing_ticket_of_the_repo_creates_none()
    {
        var alpha = NewRepo("Alpha");
        NewRepo(WorkspaceCommitter.WorkflowRepoName);
        Register(alpha, "ALP");
        using (var db = WorkflowDb.Open(dbPath)) db.Add("Alpha", "feature", TicketRules.Open, "Build A", "");
        Git(alpha, "add", "-A"); Git(alpha, "commit", "-q", "-m", "seed");
        File.WriteAllText(Path.Combine(alpha, "a.cs"), "class A {}");

        var results = Committer().Run([Project("Alpha", alpha)], "Build the A class (ALP-T1)");

        Assert.That(results, Has.Count.EqualTo(1), "nothing new in the database, so no sync commit");
        Assert.That(results[0].TicketCreated, Is.False);
        Assert.That(results[0].TicketId, Is.EqualTo("ALP-T1"));
        using var check = WorkflowDb.Open(dbPath);
        Assert.That(check.Tickets("Alpha"), Has.Count.EqualTo(1));
    }

    [Test]
    public void A_repo_missing_from_the_database_fails_loudly_and_is_not_committed()
    {
        var alpha = NewRepo("Alpha");
        var beta = NewRepo("Beta");
        NewRepo(WorkspaceCommitter.WorkflowRepoName);
        Register(alpha, "ALP");
        File.WriteAllText(Path.Combine(beta, "b.cs"), "class B {}");

        var r = Committer().Run([Project("Beta", beta)], "Add B").Single();

        Assert.That(r.Ok, Is.False);
        Assert.That(r.Detail, Does.Contain("isn't in the workflow database"));
        Assert.That(Git(beta, "status", "--porcelain"), Is.Not.Empty);
    }

    [Test]
    public void The_workflow_repo_is_committed_last_so_it_carries_the_run_s_new_tickets()
    {
        var alpha = NewRepo("Alpha");
        var wf = NewRepo(WorkspaceCommitter.WorkflowRepoName);
        Register(alpha, "ALP");
        File.WriteAllText(Path.Combine(alpha, "a.cs"), "class A {}");

        var results = Committer().Run([Project(WorkspaceCommitter.WorkflowRepoName, wf), Project("Alpha", alpha)], null);

        Assert.That(results.Select(r => r.Project), Is.EqualTo(new[] { "Alpha", WorkspaceCommitter.WorkflowRepoName }));
        Assert.That(results[1].CommitMessage, Is.EqualTo(WorkspaceCommitter.SyncMessageFor("ALP-T1")), "only database files changed there, so it cites the run's ticket instead of making its own");
        Assert.That(Git(wf, "status", "--porcelain"), Is.Empty);
        Assert.That(File.ReadAllText(Path.Combine(wf, "data", "workflow.sql")), Does.Contain("ALP-T1"));
    }

    [Test]
    public void A_database_change_made_outside_the_run_gets_a_workflow_ticket_of_its_own()
    {
        var alpha = NewRepo("Alpha");
        var wf = NewRepo(WorkspaceCommitter.WorkflowRepoName);
        Register(alpha, "ALP");
        Register(wf, "FLOW");
        using (var db = WorkflowDb.Open(dbPath)) db.Add("Alpha", "fix", TicketRules.Open, "Filed from elsewhere", "");

        var r = Committer().Run([Project(WorkspaceCommitter.WorkflowRepoName, wf)], null).Single();

        Assert.That(r.TicketCreated, Is.True);
        Assert.That(r.TicketId, Is.EqualTo("FLOW-T1"));
        Assert.That(Git(wf, "log", "-1", "--format=%s"), Does.EndWith("(FLOW-T1)"), "the commit-msg hook refuses a subject without a ticket");
        Assert.That(Git(wf, "status", "--porcelain"), Is.Empty);
        Assert.That(File.ReadAllText(Path.Combine(wf, "data", "workflow.sql")), Does.Contain("FLOW-T1"), "the dump was rewritten after the repo's own ticket");
    }

    [Test]
    public void Prose_local_agent_state_is_never_committed_and_alone_reads_as_clean()
    {
        var alpha = NewRepo("Alpha");
        NewRepo(WorkspaceCommitter.WorkflowRepoName);
        Register(alpha, "ALP");
        var prose = Path.Combine(alpha, ".prose");
        Directory.CreateDirectory(Path.Combine(prose, "commands"));
        Directory.CreateDirectory(Path.Combine(prose, "archive"));
        foreach (var f in new[] { "agent-session.json", "agent-session.json.001", "agent-queue.json", "quicksave.md", Path.Combine("archive", "old.md") })
            File.WriteAllText(Path.Combine(prose, f), "local");

        Assert.That(Committer().Run([Project("Alpha", alpha)], "Anything").Single().Detail, Is.EqualTo("nothing to commit"));

        File.WriteAllText(Path.Combine(prose, "commands", "commit.md"), "tracked");
        Committer().Run([Project("Alpha", alpha)], "Add the shared Prose commands");

        var committed = Git(alpha, "show", "--name-only", "--format=", "HEAD");
        Assert.That(committed, Does.Contain(".prose/commands/commit.md"));
        Assert.That(committed, Does.Not.Contain("agent-session").And.Not.Contain("agent-queue").And.Not.Contain("quicksave").And.Not.Contain("archive"));
    }

    private WorkspaceCommitter Committer() => new(new GitService(), root, dbPath);

    private static Project Project(string name, string path) => new() { Name = name, Path = path };

    private void Register(string repoPath, string code)
    {
        using (var db = WorkflowDb.Open(dbPath))
        {
            db.AddRepo(new Repo(Path.GetFileName(repoPath), repoPath), code);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(dbPath)!, "workflow.sql"), db.Dump());
        }
        // Like the real repo, the database files are tracked before any run touches them.
        var wf = Path.Combine(root, WorkspaceCommitter.WorkflowRepoName);
        Git(wf, "add", "-A");
        Git(wf, "commit", "-q", "-m", "seed");
    }

    private string NewRepo(string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-q");
        Git(path, "config", "user.name", "Test");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "commit.gpgsign", "false");
        return path;
    }

    private static string Git(string repo, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repo);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.Trim();
    }
}
