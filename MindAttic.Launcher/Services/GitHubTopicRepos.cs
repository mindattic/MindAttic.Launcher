using System.Net.Http.Headers;
using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Reads the authenticated GitHub user's repos carrying a given topic
/// (private and public — whatever the token can see) via the REST API. An
/// injectable <see cref="HttpClient"/> keeps this testable without a real
/// network call.
/// </summary>
public sealed class GitHubTopicRepos(HttpClient? http = null)
{
    private const int PageSize = 100;

    private readonly HttpClient http = http ?? Shared.Value;

    private static readonly Lazy<HttpClient> Shared = new(() =>
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MindAttic.Launcher");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    });

    /// <summary>
    /// Every repo's lower-cased <c>owner/repo</c> full name whose topics
    /// include <paramref name="topic"/> (case-insensitive — GitHub itself
    /// stores topic names lower-cased). Paginates <c>GET /user/repos</c> until
    /// a short page ends it. Throws <see cref="InvalidOperationException"/> on
    /// a non-success response — callers decide how to degrade (see
    /// <see cref="TopicRepoSync"/>).
    /// </summary>
    public async Task<IReadOnlySet<string>> FetchAsync(string token, string topic, CancellationToken ct = default)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"user/repos?per_page={PageSize}&page={page}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"GitHub API returned {(int)response.StatusCode} fetching repos.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            var count = 0;
            foreach (var repo in doc.RootElement.EnumerateArray())
            {
                count++;
                if (HasTopic(repo, topic) &&
                    repo.TryGetProperty("full_name", out var fullName) && fullName.GetString() is { } name)
                    result.Add(name.ToLowerInvariant());
            }

            if (count < PageSize) break;
        }

        return result;
    }

    private static bool HasTopic(JsonElement repo, string topic)
    {
        if (!repo.TryGetProperty("topics", out var topics) || topics.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var t in topics.EnumerateArray())
            if (string.Equals(t.GetString(), topic, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}
