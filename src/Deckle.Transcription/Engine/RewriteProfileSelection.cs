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
// it names no longer exists: the step is then skipped) and its moment. Empty
// when rewriting or rewriting dictations is off.
internal sealed record RewriteStepPlan(RewriteProfile? Forced, IReadOnlyList<PlannedStep> Steps)
{
    public static readonly RewriteStepPlan None = new(null, []);

    // A step that streams in this plan: a take with none has no passage to
    // send while the person speaks.
    public bool HasStreamingStep => Forced is null && Steps.Any(step => step.Streaming && step.Profile is not null);

    // The highest step rawChars reaches whose profile exists, or null below
    // the first one.
    public PlannedStep? Highest(int rawChars) => HighestWhere(rawChars, streamingOnly: false);

    // The same among the streaming steps: the profile passages are rewritten
    // with while the person speaks. A step "at Stop" crossed in between does
    // not change it (the panel goes on with the streaming step below).
    public PlannedStep? HighestStreaming(int rawChars) => HighestWhere(rawChars, streamingOnly: true);

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

    private PlannedStep? HighestWhere(int rawChars, bool streamingOnly)
    {
        for (int i = Steps.Count - 1; i >= 0; i--)
        {
            PlannedStep step = Steps[i];
            if (rawChars < step.FromChars || step.Profile is null) continue;
            if (streamingOnly && !step.Streaming) continue;
            return step;
        }
        return null;
    }
}

internal readonly record struct PlannedStep(
    int Index, int FromChars, string ProfileId, RewriteProfile? Profile, bool Streaming);

internal static class RewriteProfileSelection
{
    // streamingTranscription false: the take is transcribed in one call at
    // Stop, so nothing exists to rewrite while the person speaks and every
    // step rewrites at Stop.
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
                streamingTranscription && dictation.RewriteWhileSpeaking));
        }
        return new RewriteStepPlan(null, planned);
    }
}
