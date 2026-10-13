using System.Net;
using System.Text;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class GitHubTopicReposTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage JsonPage(params (string FullName, string[] Topics)[] repos) =>
        JsonPage(repos.Select(r => (r.FullName, r.Topics, Archived: false)).ToArray());

    private static HttpResponseMessage JsonPage(params (string FullName, string[] Topics, bool Archived)[] repos)
    {
        var items = string.Join(",", repos.Select(r =>
            $"{{\"full_name\":\"{r.FullName}\",\"archived\":{(r.Archived ? "true" : "false")}," +
            $"\"topics\":[{string.Join(",", r.Topics.Select(t => $"\"{t}\""))}]}}"));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"[{items}]", Encoding.UTF8, "application/json")
        };
    }

    [Test]
    public async Task FetchAsync_returns_lowercased_full_names_of_repos_carrying_the_topic()
    {
        using var handler = new FakeHandler(_ => JsonPage(
            ("MindAttic/Launcher", ["work-in-progress"]),
            ("someone/Other-Repo", ["work-in-progress", "dotnet"]),
            ("mindattic/Shelved", ["dotnet"])));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubTopicRepos(http);

        var result = await client.FetchAsync("test-token", "work-in-progress");

        Assert.That(result, Is.EquivalentTo(new[] { "mindattic/launcher", "someone/other-repo" }));
    }

    [Test]
    public async Task FetchAsync_excludes_archived_repos_even_if_still_tagged()
    {
        using var handler = new FakeHandler(req => req.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : JsonPage(
                ("mindattic/Active", new[] { "work-in-progress" }, Archived: false),
                ("mindattic/Shelved", new[] { "work-in-progress" }, Archived: true)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubTopicRepos(http);

        var result = await client.FetchAsync("test-token", "work-in-progress");

        Assert.That(result, Is.EquivalentTo(new[] { "mindattic/active" }));
    }

    [Test]
    public async Task FetchAsync_strips_the_stale_topic_from_an_archived_repo_on_github()
    {
        HttpRequestMessage? putRequest = null;
        string? putBody = null;
        using var handler = new FakeHandler(req =>
        {
            if (req.Method == HttpMethod.Put)
            {
                putRequest = req;
                putBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return JsonPage(
                ("mindattic/Active", new[] { "work-in-progress" }, Archived: false),
                ("mindattic/Shelved", new[] { "work-in-progress", "dotnet" }, Archived: true));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubTopicRepos(http);

        await client.FetchAsync("test-token", "work-in-progress");

        Assert.That(putRequest, Is.Not.Null);
        Assert.That(putRequest!.RequestUri!.ToString(), Is.EqualTo("https://api.github.com/repos/mindattic/Shelved/topics"));
        Assert.That(putBody, Does.Contain("dotnet"));
        Assert.That(putBody, Does.Not.Contain("work-in-progress"));
        Assert.That(client.Healed, Is.EquivalentTo(new[] { "mindattic/shelved" }));
    }

    [Test]
    public async Task FetchAsync_does_not_strip_the_topic_from_an_archived_repo_that_lacks_it()
    {
        var putCalled = false;
        using var handler = new FakeHandler(req =>
        {
            if (req.Method == HttpMethod.Put)
            {
                putCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return JsonPage(("mindattic/Shelved", new[] { "dotnet" }, Archived: true));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubTopicRepos(http);

        await client.FetchAsync("test-token", "work-in-progress");

        Assert.That(putCalled, Is.False);
        Assert.That(client.Healed, Is.Empty);
    }

    [Test]
    public async Task FetchAsync_stops_stripping_after_github_refuses_and_flags_it()
    {
        var putCount = 0;
        using var handler = new FakeHandler(req =>
        {
            if (req.Method == HttpMethod.Put)
            {
                putCount++;
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            return JsonPage(
                ("mindattic/Active", new[] { "work-in-progress" }, Archived: false),
                ("mindattic/ShelvedA", new[] { "work-in-progress" }, Archived: true),
                ("mindattic/ShelvedB", new[] { "work-in-progress" }, Archived: true));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubTopicRepos(http);

        var result = await client.FetchAsync("read-only-token", "work-in-progress");

        Assert.That(result, Is.EquivalentTo(new[] { "mindattic/active" }));
        Assert.That(putCount, Is.EqualTo(1));
        Assert.That(client.HealDenied, Is.True);
        Assert.That(client.Healed, Is.Empty);
    }

    [Test]
    public async Task FetchAsync_paginates_until_a_short_page()
    {
        var page = 0;
        using var handler = new FakeHandler(req =>
        {
            page++;
            // Full page of 100 on page 1, short page on page 2 — must stop there.
            return page == 1
                ? JsonPage(Enumerable.Range(0, 100).Select(i => ($"owner/repo{i}", new[] { "work-in-progress" })).ToArray())
                : JsonPage(("owner/last", new[] { "work-in-progress" }));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubTopicRepos(http);

        var result = await client.FetchAsync("test-token", "work-in-progress");

        Assert.That(result, Has.Count.EqualTo(101));
        Assert.That(result, Does.Contain("owner/last"));
        Assert.That(page, Is.EqualTo(2));
    }

    [Test]
    public void FetchAsync_throws_on_a_non_success_response()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubTopicRepos(http);

        Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchAsync("bad-token", "work-in-progress"));
    }
}
