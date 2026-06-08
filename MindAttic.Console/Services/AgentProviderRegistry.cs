using MindAttic.Console.Models;

namespace MindAttic.Console.Services;

public sealed class AgentProviderRegistry(SettingsStore store)
{
    public static IReadOnlyList<AgentProvider> Defaults { get; } =
    [
        new AgentProvider { Key = "Claude", Name = "Claude Code",  RunCommand = "claude --dangerously-skip-permissions --model claude-sonnet-4-6" },
        new AgentProvider { Key = "Codex",  Name = "OpenAI Codex", RunCommand = "codex --dangerously-bypass-approvals-and-sandbox" }
    ];

    public IReadOnlyList<AgentProvider> All() => ProvidersFrom(store.Load());

    private static IReadOnlyList<AgentProvider> ProvidersFrom(AppSettings settings)
    {
        // Coalesce: an explicit "agentProviders": null in settings.json
        // deserializes to null and would NRE here (same trap as Projects).
        var configured = (settings.AgentProviders ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Key)
                     && !string.IsNullOrWhiteSpace(a.Name)
                     && !string.IsNullOrWhiteSpace(a.RunCommand))
            .ToList();
        return configured.Count > 0 ? configured : Defaults;
    }

    public AgentProvider? ByKey(string? key) => ByKey(All(), key);

    private static AgentProvider? ByKey(IReadOnlyList<AgentProvider> providers, string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : providers.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    private static string DefaultKeyFrom(AppSettings settings, IReadOnlyList<AgentProvider> providers) =>
        ByKey(providers, settings.Provider) is not null ? settings.Provider! : providers[0].Key;

    // Every public accessor below resolves from one settings snapshot — the
    // providers list and the default/effective key both come out of a single
    // store.Load(), instead of re-reading (and re-filtering) settings per lookup.
    public string CurrentDefaultKey()
    {
        var settings = store.Load();
        return DefaultKeyFrom(settings, ProvidersFrom(settings));
    }

    public AgentProvider Current()
    {
        var settings = store.Load();
        var providers = ProvidersFrom(settings);
        return ByKey(providers, DefaultKeyFrom(settings, providers)) ?? providers[0];
    }

    public string EffectiveProviderKey(Project project)
    {
        var settings = store.Load();
        return EffectiveKey(settings, ProvidersFrom(settings), project);
    }

    private static string EffectiveKey(AppSettings settings, IReadOnlyList<AgentProvider> providers, Project project)
    {
        if (!string.IsNullOrWhiteSpace(project.Provider) && ByKey(providers, project.Provider) is not null)
            return project.Provider!;
        return DefaultKeyFrom(settings, providers);
    }

    public AgentProvider EffectiveProvider(Project project)
    {
        var settings = store.Load();
        var providers = ProvidersFrom(settings);
        return ByKey(providers, EffectiveKey(settings, providers, project))!;
    }

    public AgentProvider Next(string currentKey)
    {
        var providers = All();
        var idx = -1;
        for (var i = 0; i < providers.Count; i++)
            if (string.Equals(providers[i].Key, currentKey, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        // Match SetDefault/SetProjectProvider: unknown keys are a caller bug,
        // not "silently start from the first provider."
        if (idx < 0) throw new ArgumentException($"Unknown provider: {currentKey}", nameof(currentKey));
        return providers[(idx + 1) % providers.Count];
    }

    public void SetDefault(string providerKey)
    {
        if (ByKey(providerKey) is null) throw new ArgumentException($"Unknown provider: {providerKey}", nameof(providerKey));
        store.Update(s => s.Provider = providerKey);
    }

    /// <summary>
    /// Sets the model for a provider by rewriting the <c>--model</c> token in its
    /// RunCommand (see <see cref="ProviderModel"/>). A blank/null model clears the
    /// flag so the CLI uses its own default.
    /// </summary>
    public void SetModel(string providerKey, string? model)
    {
        if (ByKey(providerKey) is null) throw new ArgumentException($"Unknown provider: {providerKey}", nameof(providerKey));

        store.Update(s =>
        {
            // Defaults live in code, not the file. If nothing's configured yet,
            // materialize them (cloned, so the static Defaults aren't mutated)
            // so the model edit has a persisted home.
            if (s.AgentProviders is null || s.AgentProviders.Count == 0)
                s.AgentProviders = Defaults.Select(p => p.Clone()).ToList();

            var p = s.AgentProviders.FirstOrDefault(a =>
                        string.Equals(a.Key, providerKey, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Unknown provider: {providerKey}", nameof(providerKey));
            p.RunCommand = ProviderModel.Set(p.RunCommand, model);
        });
    }

    public void SetProjectProvider(string projectName, string? providerKey)
    {
        if (!string.IsNullOrWhiteSpace(providerKey) && ByKey(providerKey) is null)
            throw new ArgumentException($"Unknown provider: {providerKey}", nameof(providerKey));

        store.Update(s =>
        {
            var p = ProjectRoster.FindByName(s, projectName)
                ?? throw new ArgumentException($"Unknown project: {projectName}", nameof(projectName));
            p.Provider = string.IsNullOrWhiteSpace(providerKey) ? null : providerKey;
        });
    }
}
