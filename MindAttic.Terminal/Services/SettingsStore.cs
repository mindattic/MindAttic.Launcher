using System.Text.Json;
using MindAttic.Terminal.Models;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Settings;

namespace MindAttic.Terminal.Services;

/// <summary>
/// Loads and saves AppSettings via MindAttic.Vault. Settings live at
/// %APPDATA%\MindAttic\MindAttic.Terminal\settings.json. Mobile.Token is
/// resolved from the MindAttic.Mobile token bucket — never persisted in
/// settings.json itself.
/// </summary>
public sealed class SettingsStore
{
    public const string AppBucket = "MindAttic.Terminal";
    public const string MobileTokenBucket = "MindAttic.Mobile";
    public const string MobileTokenKey = "Token";

    // Legacy file that the original PowerShell scripts read from. Used as a
    // one-time seed source if the Vault settings.json is missing on first run.
    public const string DefaultLegacySettingsPath = @"D:\Projects\MindAttic\settings.json";

    private static readonly JsonSerializerOptions LegacyReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly JsonSettingsStore<AppSettings> store;
    private readonly TokenStore mobileTokens;
    private readonly string? legacySettingsPath;

    public SettingsStore()
        : this(JsonSettingsStore<AppSettings>.ForApp(AppBucket),
               TokenStore.ForBucket(MobileTokenBucket),
               DefaultLegacySettingsPath)
    {
    }

    public SettingsStore(JsonSettingsStore<AppSettings> store, TokenStore mobileTokens)
        : this(store, mobileTokens, legacySettingsPath: null)
    {
    }

    public SettingsStore(JsonSettingsStore<AppSettings> store, TokenStore mobileTokens, string? legacySettingsPath)
    {
        this.store = store;
        this.mobileTokens = mobileTokens;
        this.legacySettingsPath = legacySettingsPath;
    }

    public string SettingsFilePath => store.FilePath;

    public AppSettings Load()
    {
        if (!store.Exists())
            SeedFromLegacyIfPresent();

        var settings = store.Load();
        ApplyVaultToken(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        // Mobile.Token always lives in the Vault token bucket, never on disk in
        // settings.json. Strip it from the snapshot we persist.
        var safe = CloneWithoutToken(settings);
        store.Save(safe);
    }

    public AppSettings Update(Action<AppSettings> mutate)
    {
        return store.Update(s =>
        {
            ApplyVaultToken(s);
            mutate(s);
            s.Mobile.Token = null;
        });
    }

    public void SetMobileToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            mobileTokens.Remove(MobileTokenKey);
            return;
        }
        mobileTokens.Set(MobileTokenKey, token);
    }

    public string? GetMobileToken() => mobileTokens.Get(MobileTokenKey);

    private void ApplyVaultToken(AppSettings settings)
    {
        var token = mobileTokens.Get(MobileTokenKey);
        if (!string.IsNullOrWhiteSpace(token))
            settings.Mobile.Token = token;
    }

    private void SeedFromLegacyIfPresent()
    {
        if (string.IsNullOrWhiteSpace(legacySettingsPath)) return;
        if (!File.Exists(legacySettingsPath)) return;

        try
        {
            var raw = File.ReadAllText(legacySettingsPath);
            if (string.IsNullOrWhiteSpace(raw)) return;

            var seed = JsonSerializer.Deserialize<AppSettings>(raw, LegacyReadOptions);
            if (seed is null) return;

            // Move any legacy plaintext Mobile.Token into the Vault bucket so
            // it stops living in source-controllable JSON.
            if (!string.IsNullOrWhiteSpace(seed.Mobile.Token))
                mobileTokens.Set(MobileTokenKey, seed.Mobile.Token!);

            seed.Mobile.Token = null;
            store.Save(seed);
        }
        catch
        {
            // Best-effort seeding. A malformed legacy file falls through to a
            // default-constructed AppSettings — same posture as JsonSettingsStore.Load.
        }
    }

    private static AppSettings CloneWithoutToken(AppSettings source)
    {
        return new AppSettings
        {
            Provider = source.Provider,
            WindowsTerminalSettingsPath = source.WindowsTerminalSettingsPath,
            Mobile = new MobileBridgeSettings
            {
                ServerUrl = source.Mobile.ServerUrl,
                Token = null,
                Enabled = source.Mobile.Enabled,
                AllProjects = source.Mobile.AllProjects
            },
            AgentProviders = source.AgentProviders.Select(a => new AgentProvider
            {
                Key = a.Key,
                Name = a.Name,
                RunCommand = a.RunCommand
            }).ToList(),
            Projects = source.Projects.Select(p => new Project
            {
                Name = p.Name,
                Repo = p.Repo,
                Path = p.Path,
                Description = p.Description,
                OpenWith = p.OpenWith,
                RunCommand = p.RunCommand,
                TabAlias = p.TabAlias,
                TabColor = p.TabColor,
                ColorScheme = p.ColorScheme,
                Provider = p.Provider,
                MobileEnabled = p.MobileEnabled
            }).ToList()
        };
    }
}
