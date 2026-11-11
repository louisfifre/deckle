using System;
using System.Collections.Generic;
using Deckle.Core;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ── LLM rewriting settings ───────────────────────────────────────────────────

public sealed class RewriteProfile
{
    // Stable across renames: references and corpus telemetry join on it, never
    // on Name. LlmSettingsNormalizer.RepairProfileReferences fills it when
    // missing (the built-in defaults carry none).
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    // The LlmProvider this profile sends to, by Id.
    public string ProviderId { get; set; } = "";

    public string Model { get; set; } = "";
    public string SystemPrompt { get; set; } = "";

    // Generation hints: null = the provider's default (not sent). A hosted
    // provider may ignore them (ClaudeEngine drops them).
    public double? Temperature { get; set; }
    public int? NumCtxK { get; set; }            // in K (×1024 when sent)
    public double? TopP { get; set; }
    public double? RepeatPenalty { get; set; }

    // How much the model reasons before it answers. Per profile, not per
    // provider, because what a level allows depends on the model, which the
    // profile names. Null keeps the engine's own default (Claude: low effort,
    // adaptive thinking), so a profile saved before this setting behaves as
    // it did; the default moves only once the benchmark has measured a level.
    public ReasoningLevel? Reasoning { get; set; }

    // Starts a dictation rewritten with this profile, whatever its length.
    // Registered by the App next to the other hotkeys only while
    // HotkeyEnabled is on and the chord is complete; switching it off keeps
    // the chord, so switching back on finds it.
    public bool HotkeyEnabled { get; set; }
    public HotkeyChord? Hotkey { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasActiveHotkey => HotkeyEnabled && Hotkey is { IsComplete: true };
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

// One step of the dictation timeline (specs/deckle-dictation-rewriting.md):
// from FromChars characters of raw transcript, the dictation belongs to
// ProfileId. Streaming on: the profile rewrites passage by passage while the
// person speaks. Off: it rewrites the whole raw text at Stop.
//
// The moment lives on the step, not on the profile: one profile can stream
// at one length and wait for Stop at another, and the page sets both where
// the steps are.
public sealed class RewriteStep
{
    public string ProfileId { get; set; } = "";

    // The first step's FromChars counts only while
    // DictationRewriteSettings.StartAtLength is on; off, it is kept so that
    // ticking "Start at" again finds the length the person had set.
    public int FromChars { get; set; }

    public bool Streaming { get; set; }
}

// Which profile rewrites a dictation, and when: an ordered list of steps.
//
// The first step is the base rewrite and always exists: it starts at the
// beginning of the dictation, or at its own length with StartAtLength on.
// Every later step starts at its FromChars, strictly above the step before.
// The highest step a dictation reaches takes it, and a dictation is always
// rewritten from its beginning, never from the length of the step.
//
// Characters of raw transcript, not minutes: they are counted the same way
// whichever pipeline transcribed the take, where a duration needed one
// measure for a monolithic take (the recording) and another for a streamed
// one (the speech sent), and a silence must not make a step cross. A person
// judges by how long they spoke, so the timeline shows each length with its
// spoken duration beside it (CharsPerMinute).
public sealed class DictationRewriteSettings
{
    // Measured on 2,821 of the maintainer's dictations (latency telemetry,
    // 2026-11-06): median 786 characters a minute raw, 701 on takes over two
    // minutes, the ones the later steps are about. A display ratio only,
    // never a threshold.
    public const int CharsPerMinute = 700;

    // From about nine seconds to about forty-five minutes of speech.
    public const int MinChars = 100;
    public const int MaxChars = 30000;

    // Where the first step's "Start at" lands when nothing was set yet:
    // about a minute and a half of speech.
    public const int DefaultStartAtChars = 1000;

    // A passage: the stretch of raw transcript a streaming step rewrites in
    // one request, closed at the first pause once it reaches PassageChars
    // (DictationBatcher). 600 is about a hundred French words, forty seconds
    // of speech: enough context for the profile to condense a repeated idea,
    // short enough that the last passage at Stop is a quick one. The bounds
    // keep a passage between a long sentence and a page. Not on the page: a
    // value the app decides for the person (2026-11-11).
    public const int MinPassageChars = 200;
    public const int MaxPassageChars = 2000;

    // "Rewrite dictations". Off, a dictation arrives as said; a profile's own
    // shortcut still rewrites (RewriteProfile.HotkeyEnabled).
    public bool Enabled { get; set; }

    // The first step's "Start at": on, a dictation shorter than
    // Steps[0].FromChars stays as said.
    public bool StartAtLength { get; set; }

    // Never empty once normalized (LlmSettingsNormalizer.NormalizeDictation):
    // Steps[0] is the base rewrite. Empty in a file written before the steps
    // existed, which is what tells the normalizer to migrate it.
    public List<RewriteStep> Steps { get; set; } = new();

    public int PassageChars { get; set; } = 600;

    // Where a step starts counting, the first one at 0 unless StartAtLength.
    public int StartOf(int index) =>
        index == 0 && !StartAtLength ? 0 : Steps[index].FromChars;

    // The precision a length is held at: about a twentieth of it, on the
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

    // The nearest length on the grain of its size, within the range.
    public static int Snap(double chars)
    {
        int clamped = (int)Math.Clamp(Math.Round(chars), MinChars, MaxChars);
        int grain = GrainAt(clamped);
        int rounded = (int)Math.Round(clamped / (double)grain, MidpointRounding.AwayFromZero) * grain;
        return Math.Clamp(rounded, MinChars, MaxChars);
    }

    // The lengths next to a snapped one, on either side.
    public static int Above(int chars) => chars + GrainAt(chars);
    public static int Below(int chars) => chars - GrainAt(chars - 1);
}

public sealed class LlmSettings
{
    public bool Enabled { get; set; } = true;

    // The steps that choose the profile of a dictation and its moment.
    public DictationRewriteSettings Dictation { get; set; } = new();

    // ── Before the steps (read once, to migrate) ──────────────────────────
    //
    // The shape a file had before 2026-11-11: an active profile, a length
    // ladder and a switch for batches. LlmSettingsNormalizer.NormalizeDictation
    // turns them into Dictation the first time such a file loads.
    public BatchRewriteSettings BatchRewriting { get; set; } = new();
    public LengthSwitchSettings LengthSwitch { get; set; } = new();

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

    // Before the steps too: the profile every dictation took. null = plain
    // transcription. Lookup prefers the Id (stable across renames) and falls
    // back to the Name.
    public string? ActiveProfileId { get; set; }
    public string? ActiveProfileName { get; set; }

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
}
