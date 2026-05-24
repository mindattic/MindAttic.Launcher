using MindAttic.Console.Models;

namespace MindAttic.Console.Services;

public sealed class AgentProviderRegistry(SettingsStore store)
{
    public static IReadOnlyList<AgentProvider> Defaults { get; } =
    [
        new AgentProvider { Key = "Claude", Name = "Claude Code",  RunCommand = "claude --dangerously-skip-permissions" },
        new AgentProvider { Key = "Codex",  Name = "OpenAI Codex", RunCommand = "codex --dangerously-bypass-approvals-and-sandbox" }
    ];

    public IReadOnlyList<AgentProvider> All()
    {
        var configured = store.Load().AgentProviders
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

    public string CurrentDefaultKey()
    {
        var providers = All();
        var providerKey = store.Load().Provider;
        if (ByKey(providers, providerKey) is not null) return providerKey!;
        return providers[0].Key;
    }

    public AgentProvider Current()
    {
        var providers = All();
        return ByKey(providers, CurrentDefaultKey(providers)) ?? providers[0];
    }

    public string EffectiveProviderKey(Project project) => EffectiveProviderKey(All(), project);

    private string EffectiveProviderKey(IReadOnlyList<AgentProvider> providers, Project project)
    {
        if (!string.IsNullOrWhiteSpace(project.Provider) && ByKey(providers, project.Provider) is not null)
            return project.Provider!;
        return CurrentDefaultKey(providers);
    }

    private string CurrentDefaultKey(IReadOnlyList<AgentProvider> providers)
    {
        var providerKey = store.Load().Provider;
        if (ByKey(providers, providerKey) is not null) return providerKey!;
        return providers[0].Key;
    }

    // Single All() call per EffectiveProvider invocation — the public API was
    // routing through ByKey twice (once for the key, once for the lookup),
    // each one re-filtering the provider list from settings.
    public AgentProvider EffectiveProvider(Project project)
    {
        var providers = All();
        return ByKey(providers, EffectiveProviderKey(providers, project))!;
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
