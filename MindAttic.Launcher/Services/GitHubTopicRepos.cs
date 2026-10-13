using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MindAttic.Launcher.Services;

/// <summary>
/// Reads the authenticated GitHub user's repos carrying a given topic
/// (private and public — whatever the token can see) via the REST API. An
/// injectable <see cref="HttpClient"/> keeps this testable without a real
/// network call. Also best-effort strips the topic from archived repos that
/// still carry it (see <see cref="Healed"/>).
/// </summary>
public sealed class GitHubTopicRepos(HttpClient? http = null)
{
    private const int PageSize = 100;

    private readonly HttpClient http = http ?? Shared.Value;
    private readonly List<string> healed = [];

    /// <summary>
    /// Lower-cased <c>owner/repo</c> full names that the most recent
    /// <see cref="FetchAsync"/> found archived but still carrying the topic,
    /// and successfully stripped it from on GitHub. Cleared at the start of
    /// each call.
    /// </summary>
    public IReadOnlyList<string> Healed => healed;

    /// <summary>
    /// True when the most recent <see cref="FetchAsync"/> tried to strip a
    /// stale topic and GitHub refused it (403/404 — the token can read repos
    /// but not edit their topics). The remaining strips in that call are
    /// skipped rather than repeating a request that's bound to fail, and the
    /// caller can tell the user why the tag is still there. Cleared at the
    /// start of each call.
    /// </summary>
    public bool HealDenied { get; private set; }

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
        healed.Clear();
        HealDenied = false;
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
                if (!repo.TryGetProperty("full_name", out var fullNameEl) || fullNameEl.GetString() is not { } fullName)
                    continue;

                if (IsArchived(repo))
                {
                    if (HasTopic(repo, topic) && !HealDenied)
                        await StripStaleTopicAsync(token, fullName, GetTopics(repo), topic, ct).ConfigureAwait(false);
                    continue;
                }

                if (HasTopic(repo, topic))
                    result.Add(fullName.ToLowerInvariant());
            }

            if (count < PageSize) break;
        }

        return result;
    }

    /// <summary>
    /// GitHub keeps a repo's topics intact after archiving it, so an archived
    /// repo can still carry <see cref="TopicRepoSync.Topic"/> from before it
    /// was shelved. Archived is axiomatically not work-in-progress, so it's
    /// excluded from the result regardless of topic — and since that's a
    /// stale tag rather than a real state, <see cref="FetchAsync"/> also
    /// strips it from GitHub so the mismatch doesn't linger.
    /// </summary>
    private static bool IsArchived(JsonElement repo) =>
        repo.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True;

    private static bool HasTopic(JsonElement repo, string topic)
    {
        foreach (var t in GetTopics(repo))
            if (string.Equals(t, topic, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static List<string> GetTopics(JsonElement repo)
    {
        var result = new List<string>();
        if (!repo.TryGetProperty("topics", out var topics) || topics.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var t in topics.EnumerateArray())
            if (t.GetString() is { Length: > 0 } s)
                result.Add(s);

        return result;
    }

    /// <summary>
    /// Best-effort: <c>PUT</c>s the repo's topics back without <paramref name="topic"/>.
    /// A failure here never faults the overall fetch: a network hiccup leaves
    /// the stale tag for the next launch to retry, and a permission refusal
    /// sets <see cref="HealDenied"/> so the caller can say so.
    /// </summary>
    private async Task StripStaleTopicAsync(string token, string fullName, List<string> topics, string topic, CancellationToken ct)
    {
        var slash = fullName.IndexOf('/');
        if (slash < 0) return;

        var remaining = topics.Where(t => !string.Equals(t, topic, StringComparison.OrdinalIgnoreCase)).ToArray();
        var body = JsonSerializer.Serialize(new { names = remaining });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"repos/{fullName}/topics")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                healed.Add(fullName.ToLowerInvariant());
            else if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                HealDenied = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Stale topic stays for now; the next FetchAsync call will retry.
        }
    }
}
