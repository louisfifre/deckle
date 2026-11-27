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
    // missing (a profile written by hand in the file may carry none).
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
    // adaptive thinking).
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

// One step of the dictation timeline (specs/dictation/deckle-dictation.md):
// from FromChars characters of raw transcript, the dictation belongs to
// ProfileId. While the person speaks (DictationRewriteSettings.
// RewriteWhileSpeaking), the profile rewrites in blocks of BlockChars; with
// that switch off, it rewrites the whole raw text at Stop and BlockChars is
// kept for the day it is switched on.
//
// The block size lives on the step, not on the profile: a light profile reads
// well on a short stretch and a heavy one needs a long one to reorganize, and
// the page sets it where the lengths are.
public sealed class RewriteStep
{
    public string ProfileId { get; set; } = "";

    // The length of raw transcript from which this step takes the dictation,
    // the first step's too: a dictation shorter than it stays as said, and 0
    // rewrites every dictation from its first word.
    public int FromChars { get; set; }

    // The stretch of raw transcript this step's profile rewrites in one
    // request, closed at the first pause once it reaches this size
    // (DictationBatcher). Zero when the file has none: the normalizer fills
    // the default.
    public int BlockChars { get; set; }
}

// Which profile rewrites a dictation, and when: an ordered list of steps.
//
// The first step is the base rewrite and always exists. Every step starts at
// its FromChars, the first one included (0: from the beginning), each later
// one strictly above the step before. A length of 0 says "from the beginning".
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

    // A length is 0 (from the beginning) or from about nine seconds to about
    // forty-five minutes of speech. MinChars is also the finest grain.
    public const int MinChars = 100;
    public const int MaxChars = 30000;

    // A block: the stretch of raw transcript a step rewrites in one request
    // while the person speaks (RewriteStep.BlockChars). 600, the size a new
    // step starts with, is about a hundred French words, forty seconds of
    // speech: enough context for a light profile to condense a repeated idea,
    // short enough that the block left at Stop is a quick one. A heavy
    // profile reorganizes, and needs more: the spec's Arrangement works in
    // blocks of 6,000 (2026-11-12). The minimum keeps a block above a long
    // sentence; the maximum, about seventeen minutes of speech, is not
    // measured: past it, rewriting at Stop says the same thing better.
    public const int MinBlockChars = 200;
    public const int MaxBlockChars = 12000;
    public const int DefaultBlockChars = 600;

    // "Rewrite dictations". Off, a dictation arrives as said; a profile's own
    // shortcut still rewrites (RewriteProfile.HotkeyEnabled).
    public bool Enabled { get; set; }

    // "Rewrite while I speak", for the whole list of steps. On, the highest
    // step reached rewrites block by block while the person speaks; off,
    // every step rewrites the whole raw text at Stop. It changes no length:
    // a step starts at the same length in both modes (2026-11-12). Without
    // streaming transcription it has no effect, since nothing is transcribed
    // before Stop.
    public bool RewriteWhileSpeaking { get; set; }

    // Never empty once normalized (LlmSettingsNormalizer.NormalizeDictation):
    // Steps[0] is the base rewrite.
    public List<RewriteStep> Steps { get; set; } = new();

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

    // The nearest length on the grain of its size, within the range: 0 under
    // half the finest grain.
    public static int Snap(double chars)
    {
        int clamped = (int)Math.Clamp(Math.Round(chars), 0, MaxChars);
        int grain = GrainAt(clamped);
        int rounded = (int)Math.Round(clamped / (double)grain, MidpointRounding.AwayFromZero) * grain;
        return Math.Clamp(rounded, 0, MaxChars);
    }

    // A block size on the same grain as a length, within the block bounds.
    public static int SnapBlock(double chars) =>
        Math.Clamp(Snap(chars), MinBlockChars, MaxBlockChars);

    // The lengths next to a snapped one, on either side.
    public static int Above(int chars) => chars + GrainAt(chars);
    public static int Below(int chars) => chars - GrainAt(chars - 1);
}

public sealed class LlmSettings
{
    public bool Enabled { get; set; } = true;

    // The steps that choose the profile of a dictation and its moment.
    public DictationRewriteSettings Dictation { get; set; } = new();

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

    // Deckle ships no profile: a profile is the person's own prompt, written
    // on the Rewriting page (decided with Louis on 2026-11-21). A fresh data
    // root starts with none, and no reset brings one back.
    public List<RewriteProfile> Profiles { get; set; } = new();
}
