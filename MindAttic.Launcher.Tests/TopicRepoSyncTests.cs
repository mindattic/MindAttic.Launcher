using System.Net;
using System.Text;
using MindAttic.Launcher.Services;
using MindAttic.Vault.Credentials;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class TopicRepoSyncTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(respond(request));
        }
    }

    private string dir = "";

    [SetUp]
    public void SetUp() => dir = Path.Combine(Path.GetTempPath(), "topic-sync-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    private TokenStore StoreWith(string? token)
    {
        var store = new TokenStore(dir);
        if (token is not null) store.Set(GitHubCredentials.TokenName, token);
        return store;
    }

    private static HttpResponseMessage Page(params string[] fullNames) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "[" + string.Join(",", fullNames.Select(n =>
                    $"{{\"full_name\":\"{n}\",\"archived\":false,\"topics\":[\"{TopicRepoSync.Topic}\"]}}")) + "]",
                Encoding.UTF8, "application/json")
        };

    private static GitHubTopicRepos Client(FakeHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void FetchOrNull_returns_null_without_calling_GitHub_when_token_missing_or_blank(string? token)
    {
        var handler = new FakeHandler(_ => Page("a/b"));

        var result = TopicRepoSync.FetchOrNull(StoreWith(token), Client(handler));

        Assert.That(result, Is.Null);
        Assert.That(handler.Calls, Is.EqualTo(0));
    }

    [Test]
    public void FetchOrNull_returns_the_clients_set_on_success()
    {
        var handler = new FakeHandler(_ => Page("MindAttic/Launcher", "someone/Other"));

        var result = TopicRepoSync.FetchOrNull(StoreWith("tok"), Client(handler));

        Assert.That(result, Is.EquivalentTo(new[] { "mindattic/launcher", "someone/other" }));
    }

    private static IEnumerable<TestCaseData> Degrading()
    {
        yield return new TestCaseData(new HttpRequestException("boom")).SetName("HttpRequestException");
        yield return new TestCaseData(new InvalidOperationException("bad")).SetName("InvalidOperationException");
        yield return new TestCaseData(new TaskCanceledException("timeout")).SetName("TaskCanceledException");
    }

    [TestCaseSource(nameof(Degrading))]
    public void FetchOrNull_degrades_to_null_on_network_style_failures(Exception ex)
    {
        var handler = new FakeHandler(_ => throw ex);

        var result = TopicRepoSync.FetchOrNull(StoreWith("tok"), Client(handler));

        Assert.That(result, Is.Null);
        Assert.That(handler.Calls, Is.EqualTo(1));
    }

    [Test]
    public void FetchOrNull_lets_other_exceptions_propagate()
    {
        var handler = new FakeHandler(_ => throw new FormatException("nope"));

        Assert.Throws<FormatException>(() => TopicRepoSync.FetchOrNull(StoreWith("tok"), Client(handler)));
    }
}
