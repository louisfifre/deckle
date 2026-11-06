using System;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ── LlmSettingsMigrations, providers ─────────────────────────────────────────
//
// A settings file from the Ollama-only era has no provider list and profiles
// with no ProviderId. The repair seeds the default providers, carries the
// legacy endpoint into the Ollama entry (the only place it was ever used),
// and points every unassigned profile at it, so the first launch after the
// upgrade rewrites exactly as the last one before it did.
//
// Idempotent: every step acts on an empty value or a legacy field that is
// still set, so a file written after the migration is never touched again.
public static partial class LlmSettingsMigrations
{
    public static bool RepairProviders(LlmSettings s)
    {
        bool mutated = false;

        if (s.Providers.Count == 0)
        {
            s.Providers = new LlmSettings().Providers;
            LlmProvider? ollama = s.Providers.Find(p => p.Kind == LlmProviderKind.Ollama);
            if (ollama is not null && !string.IsNullOrWhiteSpace(s.OllamaEndpoint))
                ollama.Endpoint = s.OllamaEndpoint;
            mutated = true;
        }

        // Recovery for a file an earlier build already migrated: that build
        // dropped the custom endpoint from the provider (see LlmSettings
        // .OnDeserializing) but kept writing the legacy field, which is then
        // the last copy of it. A legacy value other than the default, next to
        // an Ollama provider still on the default, is adopted before the
        // legacy field is cleared. Accepted trade-off: someone who deliberately
        // reset the provider to the default while a stale custom value sat in
        // the legacy field gets that value back once.
        string defaultEndpoint = new LlmSettings().Providers.Find(p => p.Kind == LlmProviderKind.Ollama)!.Endpoint;
        LlmProvider? providerOllama = s.Providers.Find(p => p.Kind == LlmProviderKind.Ollama);
        if (providerOllama is not null
            && !string.IsNullOrWhiteSpace(s.OllamaEndpoint)
            && s.OllamaEndpoint != defaultEndpoint
            && providerOllama.Endpoint == defaultEndpoint)
        {
            providerOllama.Endpoint = s.OllamaEndpoint;
            mutated = true;
        }

        // The legacy endpoint has done its job, or is stale next to providers
        // the user already edited: either way it leaves the file.
        if (s.OllamaEndpoint is not null)
        {
            s.OllamaEndpoint = null;
            mutated = true;
        }

        foreach (var p in s.Providers)
        {
            if (string.IsNullOrWhiteSpace(p.Id))
            {
                p.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
                mutated = true;
            }
        }

        string? legacyProviderId = s.Providers.Find(p => p.Kind == LlmProviderKind.Ollama)?.Id;
        if (legacyProviderId is not null)
        {
            foreach (var profile in s.Profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.ProviderId))
                {
                    profile.ProviderId = legacyProviderId;
                    mutated = true;
                }
            }
        }

        return mutated;
    }

    /// <summary>Every repair in order; the postLoadMigration hook of the
    /// settings store, and what a reset runs on fresh defaults.</summary>
    public static bool Apply(LlmSettings s)
    {
        bool profiles = RepairProfileReferences(s);
        bool providers = RepairProviders(s);
        bool ladder = MoveProfileThresholdsToLadder(s);
        return profiles || providers || ladder;
    }
}
