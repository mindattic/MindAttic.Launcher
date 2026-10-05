using System.Net;
using System.Text;
using MindAttic.Launcher.Services;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class GitHubStarredReposTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage JsonPage(params string[] fullNames)
    {
        var items = string.Join(",", fullNames.Select(n => $"{{\"full_name\":\"{n}\"}}"));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"[{items}]", Encoding.UTF8, "application/json")
        };
    }

    [Test]
    public async Task FetchAsync_returns_lowercased_full_names_from_a_single_short_page()
    {
        using var handler = new FakeHandler(_ => JsonPage("MindAttic/Launcher", "someone/Other-Repo"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubStarredRepos(http);

        var result = await client.FetchAsync("test-token");

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
                ? JsonPage(Enumerable.Range(0, 100).Select(i => $"owner/repo{i}").ToArray())
                : JsonPage("owner/last");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubStarredRepos(http);

        var result = await client.FetchAsync("test-token");

        Assert.That(result, Has.Count.EqualTo(101));
        Assert.That(result, Does.Contain("owner/last"));
        Assert.That(page, Is.EqualTo(2));
    }

    [Test]
    public void FetchAsync_throws_on_a_non_success_response()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubStarredRepos(http);

        Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchAsync("bad-token"));
    }
}
