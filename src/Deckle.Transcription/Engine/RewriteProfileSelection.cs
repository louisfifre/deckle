using Deckle.Llm.Rewrite;

namespace Deckle.Transcription;

// The steps one take is rewritten by (specs/deckle-dictation-rewriting.md),
// read from the settings once, when the take starts, so a change made in
// Settings during the recording cannot send part of the take to a profile it
// was never meant for.
//
// Forced: a profile whose own shortcut started the take. It rewrites the
// whole raw text at Stop, whatever the length and the steps, and even with
// rewriting switched off: the press asked for that rewrite.
//
// Steps: in order, each with where it starts, its profile (null when the one
// it names no longer exists: the step is then skipped) and its block size.
// Empty when rewriting or rewriting dictations is off.
//
// WhileSpeaking: "Rewrite while I speak" holds for this take, on streaming
// transcription only. On, the highest step reached rewrites block by block
// while the person speaks, one step at a time; off, it rewrites the whole
// raw text at Stop. The lengths are the same either way.
internal sealed record RewriteStepPlan(
    RewriteProfile? Forced, IReadOnlyList<PlannedStep> Steps, bool WhileSpeaking = false)
{
    public static readonly RewriteStepPlan None = new(null, []);

    // Whether blocks leave while the person speaks: never for a profile's
    // own shortcut, nor without a step whose profile exists.
    public bool RewritesWhileSpeaking =>
        Forced is null && WhileSpeaking && Steps.Any(step => step.Profile is not null);

    // The block size text closes at before any step is reached: the first
    // step that can be reached's, so that reaching it finds the text already
    // cut its way.
    public int FirstBlockChars =>
        Steps.FirstOrDefault(step => step.Profile is not null).BlockChars is var chars and > 0
            ? chars
            : DictationRewriteSettings.DefaultBlockChars;

    // The highest step rawChars reaches whose profile exists, or null below
    // the first one: the step that takes the dictation, and while the person
    // speaks the one rewriting.
    public PlannedStep? Highest(int rawChars)
    {
        for (int i = Steps.Count - 1; i >= 0; i--)
        {
            PlannedStep step = Steps[i];
            if (rawChars >= step.FromChars && step.Profile is not null) return step;
        }
        return null;
    }

    // The profile a reached step names and that no longer exists, when it
    // stands above the step that takes the dictation: that step was skipped.
    public string? MissingProfileAbove(int rawChars)
    {
        PlannedStep? taken = Highest(rawChars);
        for (int i = Steps.Count - 1; i >= 0; i--)
        {
            PlannedStep step = Steps[i];
            if (taken is { } t && step.Index <= t.Index) break;
            if (rawChars >= step.FromChars && step.Profile is null) return step.ProfileId;
        }
        return null;
    }
}

internal readonly record struct PlannedStep(
    int Index, int FromChars, string ProfileId, RewriteProfile? Profile, int BlockChars);

internal static class RewriteProfileSelection
{
    // streamingTranscription false: the take is transcribed in one call at
    // Stop, so nothing exists to rewrite while the person speaks and every
    // step rewrites at Stop, whatever "Rewrite while I speak" says.
    public static RewriteStepPlan ForTake(
        LlmSettings settings, string? forcedProfileId, bool streamingTranscription)
    {
        if (!string.IsNullOrEmpty(forcedProfileId)
            && settings.Profiles.Find(profile => profile.Id == forcedProfileId) is { } forced)
            return new RewriteStepPlan(forced, []);

        DictationRewriteSettings? dictation = settings.Dictation;
        if (!settings.Enabled || dictation is not { Enabled: true } || dictation.Steps is not { Count: > 0 } steps)
            return RewriteStepPlan.None;

        var planned = new List<PlannedStep>(steps.Count);
        for (int i = 0; i < steps.Count; i++)
        {
            RewriteStep step = steps[i];
            planned.Add(new PlannedStep(
                i,
                dictation.StartOf(i),
                step.ProfileId,
                settings.Profiles.Find(profile => profile.Id == step.ProfileId),
                // The core does not clamp: a hand-edited settings file could
                // hold any value, and DictationBatcher rejects zero or less.
                Math.Clamp(step.BlockChars, DictationRewriteSettings.MinBlockChars, DictationRewriteSettings.MaxBlockChars)));
        }
        return new RewriteStepPlan(null, planned, streamingTranscription && dictation.RewriteWhileSpeaking);
    }
}
