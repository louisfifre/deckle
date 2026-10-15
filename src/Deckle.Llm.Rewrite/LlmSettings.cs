using System.Collections.Generic;
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

public sealed class LlmSettings
{
    public bool Enabled { get; set; } = true;

    // Per-batch rewriting during a dictation; applies the active profile to
    // each batch, then the pasted text is the batches' rewrites in order.
    public BatchRewriteSettings BatchRewriting { get; set; } = new();

    // Legacy: the one endpoint of the Ollama-only era. Kept so an older
    // settings.json deserializes without loss; RepairProviders copies it into
    // the Ollama provider once. Never read at runtime.
    public string OllamaEndpoint { get; set; } = "http://localhost:11434/api/generate";

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

    // Legacy primary slot, migrated into ActiveProfile* on load, kept so an
    // older settings.json deserializes without loss. Never read at runtime.
    public string? PrimaryRewriteProfileId { get; set; }
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

    // Legacy auto-rules, retained so an older settings file deserializes
    // without loss. Never evaluated, and a new file starts with none.
    public List<AutoRewriteRule> AutoRewriteRules { get; set; } = new();

    // Legacy auto-rule metric, retained for settings-file compatibility.
    public string RuleMetric { get; set; } = "Duration";

    // Legacy word-based auto-rules, retained for settings-file compatibility.
    public List<AutoRewriteRuleByWords> AutoRewriteRulesByWords { get; set; } = new();
}
