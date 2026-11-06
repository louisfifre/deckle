using Deckle.Audio;

namespace Deckle.Playground;

// What went wrong with the HUD tab's microphone preview, in the terms the tab
// words for its reader. The problem, its code and its log spelling are the
// generic feed's (MicrophoneLevelFeedFailure, in Deckle.Audio); the sentence
// the tab shows is the Playground's own.
internal readonly record struct MicrophonePreviewFailure(
    MicrophoneProblem Problem, uint MmsysErr = 0, string ExceptionType = "")
{
    public MicrophonePreviewFailure(MicrophoneLevelFeedFailure failure)
        : this(failure.Problem, failure.MmsysErr, failure.ExceptionType)
    {
    }

    // The name a log line carries for the problem, spelled once, by the feed.
    public string Cause => new MicrophoneLevelFeedFailure(Problem, MmsysErr, ExceptionType).Cause;

    // What the tab tells the person, one sentence about the microphone. The
    // error code shows only where the problem has nothing plainer to say; the
    // exception's type and message stay out of the window.
    public string Reason => Problem switch
    {
        MicrophoneProblem.NotDetected => "No microphone found.",
        MicrophoneProblem.InUse       => "The microphone is in use by another app.",
        MicrophoneProblem.Unavailable => $"The microphone can't be opened (error {MmsysErr}).",
        MicrophoneProblem.Ended       => "The microphone stopped sending sound.",
        _                             => "The microphone failed unexpectedly.",
    };
}
