using System;
using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ── LLM rewriting settings ───────────────────────────────────────────────────

public sealed class RewriteProfile
{
    // Stable across renames: references and corpus telemetry join on it, never
    // on Name. LlmSettingsMigrations.RepairProfileReferences fills it when
    // missing.
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    // The LlmProvider this profile sends to, by Id. Empty on a settings file
    // written before providers existed; LlmSettingsMigrations.RepairProviders
    // points it at the Ollama provider, the only engine that era had.
    public string ProviderId { get; set; } = "";

    public string Model { get; set; } = "";
    public string SystemPrompt { get; set; } = "";

    // Generation hints: null = the provider's default (not sent). A hosted
    // provider may ignore them (ClaudeEngine drops them).
    public double? Temperature { get; set; }
    public int? NumCtxK { get; set; }            // in K (×1024 when sent)
    public double? TopP { get; set; }
    public double? RepeatPenalty { get; set; }

    // Legacy per-profile length threshold (v0.77 to v0.79), retained so such a
    // file deserializes; LlmSettingsMigrations.MoveProfileThresholdsToLadder
    // turns it into a LengthSwitch step once and clears AutoSelect. A
    // threshold only means something against the others, so it now lives in
    // one page-level ladder. Never read at runtime.
    public bool AutoSelect { get; set; }
    public int AutoSelectFromMinutes { get; set; } = 5;

    // Starts a dictation rewritten with this profile, whatever its length.
    // Registered by the App next to the other hotkeys only while
    // HotkeyEnabled is on and the chord is complete; switching it off keeps
    // the chord, so switching back on finds it.
    // null only in a file written before the switch existed:
    // LlmSettingsMigrations.MoveProfileThresholdsToLadder sets it on when
    // that file already held a chord, so an existing shortcut keeps working.
    public bool? HotkeyEnabled { get; set; }
    public HotkeyChord? Hotkey { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasActiveHotkey => HotkeyEnabled == true && Hotkey is { IsComplete: true };
}

// One step of the length ladder: a dictation whose raw transcript holds at
// least FromChars characters is rewritten with ProfileId, the highest step
// reached winning. Below the first step the active profile applies.
public sealed class LengthStep
{
    public int FromChars { get; set; }
    public string ProfileId { get; set; } = "";
}

// Picking the rewrite profile by the length of the dictation.
//
// Characters of raw transcript, not minutes: they are counted the same way
// whichever pipeline transcribed the take, where a duration needed one
// measure for a monolithic take (the recording) and another for a streamed
// one (the speech sent). A person judges by how long they spoke, so the page
// shows each step with its spoken duration beside it (CharsPerMinute).
//
// Not applied to a take rewritten batch by batch: its profile is fixed when
// the first batch leaves, before any length is known.
public sealed class LengthSwitchSettings
{
    // Measured on 2,821 of the maintainer's dictations (latency telemetry,
    // 2026-11-06): median 786 characters a minute raw, 701 on takes over two
    // minutes, the ones a ladder is about. A display ratio only, never a
    // threshold.
    public const int CharsPerMinute = 700;

    // From about nine seconds to about forty-five minutes of speech.
    public const int MinChars = 100;
    public const int MaxChars = 30000;

    public bool Enabled { get; set; }

    // Sorted by FromChars, strictly increasing; each one on the grain of its
    // length (GrainAt), between MinChars and MaxChars.
    public List<LengthStep> Steps { get; set; } = new();

    // The precision a threshold is held at: about a twentieth of it, on the
    // 1-2-5 ladder, never under MinChars. A length is meant in relative
    // terms: 500 against 600 characters is another dictation, 20,000 against
    // 20,100 is not. That gives 100 up to 4,000, then 200, 500 from 10,000
    // and 1,000 from 20,000. Each band starts on a multiple of its own grain,
    // so snapping a snapped value changes nothing.
    public static int GrainAt(int chars)
    {
        int target = Math.Max(chars / 20, MinChars);
        int grain = MinChars;
        for (int decade = MinChars; ; decade *= 10)
            foreach (int factor in (ReadOnlySpan<int>)[1, 2, 5])
            {
                if (decade * factor > target) return grain;
                grain = decade * factor;
            }
    }

    // The nearest threshold on the grain of its length, within the range.
    public static int Snap(double chars)
    {
        int clamped = (int)Math.Clamp(Math.Round(chars), MinChars, MaxChars);
        int grain = GrainAt(clamped);
        int rounded = (int)Math.Round(clamped / (double)grain, MidpointRounding.AwayFromZero) * grain;
        return Math.Clamp(rounded, MinChars, MaxChars);
    }

    // The thresholds next to a snapped one, on either side.
    public static int Above(int chars) => chars + GrainAt(chars);
    public static int Below(int chars) => chars - GrainAt(chars - 1);
}

// Legacy duration-rule shape, retained so existing settings deserialize without
// loss. Never evaluated: rewriting applies the active profile only.
public sealed class AutoRewriteRule
{
    public int MinDurationSeconds { get; set; } = 0;

    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
}

// Legacy word-rule shape, retained so existing settings deserialize without
// loss. Never evaluated: rewriting applies the active profile only.
public sealed class AutoRewriteRuleByWords
{
    public int MinWordCount { get; set; } = 0;
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
}

// Rewriting while the dictation runs, batch by batch, instead of once at Stop.
// Off by default: it costs one request per batch and the seams between
// batches are a quality risk the whole-take rewrite does not have. The
// threshold is in characters of raw transcript: a batch closes at the first
// pause once the accumulated text reaches it (DictationBatcher). 600 is about
// a hundred French words, forty seconds of speech: enough context for the
// profile to condense a repeated idea, short enough that the last batch at
// Stop is a quick one. The bounds keep a batch between a long sentence and
// a page.
public sealed class BatchRewriteSettings
{
    public const int MinThresholdChars = 200;
    public const int MaxThresholdChars = 2000;

    public bool Enabled { get; set; } = false;
    public int ThresholdChars { get; set; } = 600;
}

public sealed class LlmSettings : System.Text.Json.Serialization.IJsonOnDeserializing
{
    public bool Enabled { get; set; } = true;

    // Per-batch rewriting during a dictation; applies the active profile to
    // each batch, then the pasted text is the batches' rewrites in order.
    public BatchRewriteSettings BatchRewriting { get; set; } = new();

    // Another profile than the active one past a given length.
    public LengthSwitchSettings LengthSwitch { get; set; } = new();

    // Legacy: the one endpoint of the Ollama-only era. Read from an older
    // settings.json only; RepairProviders copies it into the Ollama provider
    // and clears it, and null is not written, so a migrated file no longer
    // carries it. Never read at runtime.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? OllamaEndpoint { get; set; }

    // A file read from disk starts with no providers, not with the three
    // defaults below: System.Text.Json keeps a property's initializer when the
    // key is absent, so a file from the Ollama-only era (no `providers` key)
    // would otherwise look already migrated, and RepairProviders would never
    // carry its custom OllamaEndpoint into the Ollama provider. Empty means
    // "the file had none"; RepairProviders then seeds them.
    void System.Text.Json.Serialization.IJsonOnDeserializing.OnDeserializing() => Providers = new();

    // Where a model is served from. A profile names one by Id. These defaults
    // are the three shapes the market has (a local Ollama, a chat-completions
    // server, Claude); the user renames, repoints, disables or deletes them in
    // Settings, and a Claude key goes to the vault, never here.
    public List<LlmProvider> Providers { get; set; } = new()
    {
        new() { Id = "ollama",    Name = "Ollama",    Kind = LlmProviderKind.Ollama,           Endpoint = "http://localhost:11434/api/generate" },
        new() { Id = "lm-studio", Name = "LM Studio", Kind = LlmProviderKind.OpenAiCompatible, Endpoint = "http://localhost:1234/v1" },
        new() { Id = "claude",    Name = "Claude",    Kind = LlmProviderKind.Claude },
    };

    // The one persisted choice applied to every dictation when Enabled.
    // null = plain transcription (opt-in by default). Lookup prefers the Id
    // (stable across renames) and falls back to the Name;
    // LlmSettingsMigrations.RepairProfileReferences keeps the pair in sync.
    public string? ActiveProfileId { get; set; }
    public string? ActiveProfileName { get; set; }

    // Legacy primary slot, migrated into ActiveProfile* on load and cleared;
    // null is not written. Never read at runtime.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PrimaryRewriteProfileId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PrimaryRewriteProfileName { get; set; }

    // Two depths of intervention, decided with Louis on 2026-10-15. The quick
    // correction (a misheard word, an accent) is the ASR's job, so no profile
    // for it. Lissage keeps the prose as spoken and removes what writing does
    // not carry; Réécriture restructures a dictation into a text its reader
    // takes in at once. Both keep the speaker's words and both keep the two
    // moments of a change of mind, since the first one is a nuance, not a
    // slip. Both write in French because the dictations are French.
    //
    // They send to the Claude provider on Sonnet, the first model Louis wants
    // to judge the task on; Haiku is the next candidate. No sampling hints:
    // a hosted model rejects them. Reset Profiles restores exactly these.
    public List<RewriteProfile> Profiles { get; set; } = new()
    {
        new()
        {
            Name = "Lissage",
            ProviderId = "claude",
            Model = "claude-sonnet-5-5",
            SystemPrompt =
                """
                Tu reçois la transcription brute d'une dictée en français. Tu la rends en prose écrite propre, dans les mots du locuteur, sans rien résumer et sans rien ajouter.

                Ce que tu enlèves : les hésitations (euh, hum, ben), les tics (du coup, en fait, tu vois, voilà), les faux départs aussitôt repris, les mots doublés par le débit oral, et les erreurs que la reconnaissance vocale a laissées (mot entendu de travers, accord, ponctuation).

                Ce que tu condenses : une même idée redite à plusieurs endroits avec les mêmes mots se dit une seule fois, à sa première place, avec les précisions que les autres mentions apportaient.

                Ce que tu gardes : chaque idée, exemple, chiffre, nom, terme technique, intention, et chaque nuance d'incertitude (peut-être, je crois, il me semble). Quand le locuteur change d'avis, tu gardes les deux temps dans l'ordre : le premier formulé comme ce qu'il pensait d'abord, le second comme ce qu'il retient. Tu ne déplaces rien : l'ordre du discours est l'ordre de la sortie.

                Registre : les mots du locuteur, sans synonyme, sans montée en registre. Les termes anglais restent tels quels.

                Forme : de la prose, en paragraphes séparés d'une ligne vide au rythme des changements d'idée. Pas de markdown, pas de liste, pas de titre. Tu commences par le premier mot du contenu et tu finis par le dernier : aucune introduction, aucune conclusion, aucun commentaire. La sortie n'est jamais plus longue que l'entrée.
                """
        },
        new()
        {
            Name = "Réécriture",
            ProviderId = "claude",
            Model = "claude-sonnet-5-5",
            SystemPrompt =
                """
                Tu reçois la transcription brute d'une dictée en français : une pensée à voix haute, avec ses détours. Tu la réécris en un texte structuré que son destinataire lit d'un trait, avec les mots et les intentions du locuteur, sans rien résumer et sans rien ajouter.

                Structure : tu repères les sujets que le discours traverse et tu regroupes sous chacun tout ce qui le concerne, même dit à plusieurs endroits. Chaque sujet devient une section avec un titre court, suivie de paragraphes, ou d'une liste quand le locuteur énumère. Les demandes, les décisions et les questions ouvertes ressortent clairement, chacune à sa place.

                Flou : un référent vague (ça, le truc, ce machin, là-bas) est remplacé par ce qu'il désigne quand le contexte le dit. Quand le contexte ne le dit pas, tu le laisses et tu le signales entre crochets : [référent à préciser]. Tu n'inventes jamais ce que le locuteur n'a pas dit.

                Changements d'avis : tu gardes les deux temps. Le premier est noté comme une piste écartée, le second comme ce que le locuteur retient. Une idée redite se dit une seule fois, avec toutes les précisions que ses répétitions apportaient. Les nuances d'incertitude (peut-être, je crois, il me semble) restent.

                Registre : les mots du locuteur, sans synonyme, sans montée en registre, sans ton corporate. Les termes anglais restent tels quels. La première personne est conservée.

                Forme : markdown léger, titres de niveau 2, listes à puces seulement pour les énumérations. Tu commences par le premier titre et tu finis par le dernier mot du contenu : aucune introduction, aucune conclusion, aucun commentaire sur le texte.
                """
        }
    };

    // Legacy auto-rules, retained so an older settings file round-trips
    // without loss (RepairProfileReferences keeps their profile pairs in
    // step). Never evaluated. null in a new file, and null is not written, so
    // only a file that already held them carries them.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<AutoRewriteRule>? AutoRewriteRules { get; set; }

    // Legacy auto-rule metric; same rule as the auto-rules above.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleMetric { get; set; }

    // Legacy word-based auto-rules; same rule as the auto-rules above.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<AutoRewriteRuleByWords>? AutoRewriteRulesByWords { get; set; }
}
