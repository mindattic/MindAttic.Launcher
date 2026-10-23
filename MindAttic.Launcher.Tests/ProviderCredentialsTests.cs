using System.Diagnostics;
using MindAttic.Launcher.Services;
using MindAttic.Vault.Credentials;
using NUnit.Framework;

namespace MindAttic.Launcher.Tests;

[TestFixture]
public sealed class ProviderCredentialsTests
{
    private sealed class FakeCredentialStore(Dictionary<string, string>? seed = null) : ICredentialStore
    {
        private readonly Dictionary<string, string> keys = new(seed ?? new(), StringComparer.OrdinalIgnoreCase);

        public string Directory => "(fake)";
        public string ProvidersFilePath => "(fake)";
        public bool ProvidersFileExists() => keys.Count > 0;
        public string? GetKey(string providerId) => keys.GetValueOrDefault(providerId);
        public void SetKey(string providerId, string apiKey) => keys[providerId] = apiKey;
        public Dictionary<string, string> LoadAll() => new(keys, StringComparer.OrdinalIgnoreCase);
        public List<string> ListProviders() => keys.Keys.ToList();
        public Dictionary<string, string> LoadAllRaw() => new(StringComparer.OrdinalIgnoreCase);
        public void SaveAllRaw(IDictionary<string, string> providers) { }
        public void SaveRaw(string providerId, string rawProviderJson) { }
    }

    [Test]
    public void Apply_sets_the_env_var_for_a_provider_with_a_vault_key()
    {
        var store = new FakeCredentialStore(new() { ["gemini"] = "AIza-test-key" });
        var psi = new ProcessStartInfo("gemini");

        ProviderCredentials.Apply(psi, "Gemini", store);

        Assert.That(psi.Environment["GEMINI_API_KEY"], Is.EqualTo("AIza-test-key"));
    }

    [Test]
    public void Apply_is_a_noop_when_vault_has_no_key_for_the_provider()
    {
        var store = new FakeCredentialStore();
        var psi = new ProcessStartInfo("gemini");
        psi.Environment.Remove("GEMINI_API_KEY");

        ProviderCredentials.Apply(psi, "Gemini", store);

        Assert.That(psi.Environment.ContainsKey("GEMINI_API_KEY"), Is.False);
    }

    [Test]
    public void Apply_sets_the_env_var_for_Claude_when_not_logged_into_claude_ai()
    {
        var store = new FakeCredentialStore(new() { ["claude"] = "sk-ant-test-key" });
        var psi = new ProcessStartInfo("claude");
        var missingCredentialsPath = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"), ".credentials.json");

        ProviderCredentials.Apply(psi, "Claude", store, claudeCredentialsPath: missingCredentialsPath, billingModePath: MissingPath("billing.json"));

        Assert.That(psi.Environment["ANTHROPIC_API_KEY"], Is.EqualTo("sk-ant-test-key"));
    }

    private static string MissingPath(string name) =>
        Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"), name);

    /// <summary>A temp billing.json with <paramref name="mode"/>, and a live claude.ai login beside it.</summary>
    private static (string Dir, string Billing, string Credentials) BillingFixture(string mode)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var billing = Path.Combine(dir, "billing.json");
        File.WriteAllText(billing, $$"""{ "mode": "{{mode}}", "since": 1760803200.0, "by": "cli" }""");
        var credentials = Path.Combine(dir, ".credentials.json");
        File.WriteAllText(credentials, """{ "claudeAiOauth": { "accessToken": "fake-session-token" } }""");
        return (dir, billing, credentials);
    }

    [Test]
    public void Apply_pushes_the_Claude_key_in_api_mode_even_with_a_claude_ai_login()
    {
        var (dir, billing, credentials) = BillingFixture("api");
        try
        {
            var store = new FakeCredentialStore(new() { ["claude"] = "sk-ant-api-mode" });
            var psi = new ProcessStartInfo("claude");

            ProviderCredentials.Apply(psi, "Claude", store, claudeCredentialsPath: credentials, billingModePath: billing);

            Assert.That(psi.Environment["ANTHROPIC_API_KEY"], Is.EqualTo("sk-ant-api-mode"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public void Apply_strips_an_inherited_Claude_key_in_team_mode()
    {
        var (dir, billing, _) = BillingFixture("team");
        try
        {
            var store = new FakeCredentialStore(new() { ["claude"] = "sk-ant-should-be-ignored" });
            var psi = new ProcessStartInfo("claude");
            psi.Environment["ANTHROPIC_API_KEY"] = "sk-ant-inherited";

            // No claude.ai login at all: team still never pushes the key.
            ProviderCredentials.Apply(psi, "Claude", store, claudeCredentialsPath: MissingPath(".credentials.json"), billingModePath: billing);

            Assert.That(psi.Environment.ContainsKey("ANTHROPIC_API_KEY"), Is.False);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public void A_failover_bills_its_own_billing_whatever_the_mode_says()
    {
        // M.LNCH-T67: billing.json says team, but this one CLI failed over to the API key, and back.
        var (dir, billing, credentials) = BillingFixture("team");
        try
        {
            var store = new FakeCredentialStore(new() { ["claude"] = "sk-ant-failover" });
            var psi = new ProcessStartInfo("claude");

            ProviderCredentials.Apply(psi, "Claude", store, claudeCredentialsPath: credentials, billingModePath: billing, claudeBilling: ClaudeBillingMode.Api);
            Assert.That(psi.Environment["ANTHROPIC_API_KEY"], Is.EqualTo("sk-ant-failover"));

            File.WriteAllText(billing, """{ "mode": "api" }""");
            ProviderCredentials.Apply(psi, "Claude", store, claudeCredentialsPath: credentials, billingModePath: billing, claudeBilling: ClaudeBillingMode.Team);
            Assert.That(psi.Environment.ContainsKey("ANTHROPIC_API_KEY"), Is.False);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public void An_unknown_billing_mode_keeps_the_login_rule()
    {
        var (dir, billing, credentials) = BillingFixture("bitcoin");
        try
        {
            var store = new FakeCredentialStore(new() { ["claude"] = "sk-ant-should-be-ignored" });
            var psi = new ProcessStartInfo("claude");
            psi.Environment.Remove("ANTHROPIC_API_KEY");

            ProviderCredentials.Apply(psi, "Claude", store, claudeCredentialsPath: credentials, billingModePath: billing);

            Assert.That(psi.Environment.ContainsKey("ANTHROPIC_API_KEY"), Is.False);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public void Apply_never_sets_ANTHROPIC_API_KEY_when_logged_into_claude_ai()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var credentialsPath = Path.Combine(dir, ".credentials.json");
        try
        {
            File.WriteAllText(credentialsPath, """{ "claudeAiOauth": { "accessToken": "fake-session-token" } }""");

            var store = new FakeCredentialStore(new() { ["claude"] = "sk-ant-should-be-ignored" });
            var psi = new ProcessStartInfo("claude");
            psi.Environment.Remove("ANTHROPIC_API_KEY");

            ProviderCredentials.Apply(psi, "Claude", store, claudeCredentialsPath: credentialsPath, billingModePath: MissingPath("billing.json"));

            Assert.That(psi.Environment.ContainsKey("ANTHROPIC_API_KEY"), Is.False);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Apply_sets_the_env_var_for_Codex()
    {
        var store = new FakeCredentialStore(new() { ["codex"] = "sk-openai-test-key" });
        var psi = new ProcessStartInfo("codex");

        ProviderCredentials.Apply(psi, "Codex", store);

        Assert.That(psi.Environment["OPENAI_API_KEY"], Is.EqualTo("sk-openai-test-key"));
    }

    [Test]
    public void Apply_never_forwards_a_key_for_a_provider_with_no_injection_mapping()
    {
        // A provider key with no env-var mapping and not Kimi — a Vault entry
        // for it (however it got there) must never be pushed anywhere.
        var store = new FakeCredentialStore(new() { ["grok"] = "sk-should-be-ignored" });
        var psi = new ProcessStartInfo("grok");

        ProviderCredentials.Apply(psi, "Grok", store);

        Assert.That(psi.Environment.Values, Does.Not.Contain("sk-should-be-ignored"));
    }

    [Test]
    public void Apply_does_not_throw_for_Kimi_even_when_config_toml_is_absent()
    {
        var store = new FakeCredentialStore(new() { ["kimi"] = "sk-kimi-test" });
        var psi = new ProcessStartInfo("kimi");
        // A path under a fresh temp dir — this must NEVER be the real
        // KimiConfigSync.DefaultConfigPath (~/.kimi-code/config.toml). Hitting
        // the real file here previously overwrote a working Kimi install with a
        // throwaway test value; every Kimi-path test must redirect the config
        // path explicitly instead of relying on the machine not having one.
        var missingConfigPath = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"), "config.toml");

        Assert.DoesNotThrow(() => ProviderCredentials.Apply(psi, "Kimi", store, missingConfigPath));
    }

    [Test]
    public void Apply_syncs_the_kimi_key_into_the_given_config_path_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MindAttic.Launcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var configPath = Path.Combine(dir, "config.toml");
        try
        {
            File.WriteAllText(configPath, string.Join('\n',
            [
                "[providers.\"managed:kimi-code\"]",
                "type = \"kimi\"",
                "api_key = \"\"",
            ]));

            var store = new FakeCredentialStore(new() { ["kimi"] = "sk-kimi-real" });
            var psi = new ProcessStartInfo("kimi");

            ProviderCredentials.Apply(psi, "Kimi", store, configPath);

            Assert.That(File.ReadAllText(configPath), Does.Contain("api_key = \"sk-kimi-real\""));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
