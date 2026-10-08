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

    private static HttpResponseMessage JsonPage(params (string FullName, string[] Topics)[] repos)
    {
        var items = string.Join(",", repos.Select(r =>
            $"{{\"full_name\":\"{r.FullName}\",\"topics\":[{string.Join(",", r.Topics.Select(t => $"\"{t}\""))}]}}"));
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
