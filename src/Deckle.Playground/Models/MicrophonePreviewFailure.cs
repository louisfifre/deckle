namespace Deckle.Playground;

// What went wrong with the HUD tab's microphone preview, in the terms the tab
// words for its reader. A failed open keeps the kind the pre-flight probe gives
// the same error code (MicrophoneCapture.ClassifyError); the last two are the
// capture ending, or throwing, with no error code to read.
internal enum MicrophoneProblem
{
    NotDetected,
    InUse,
    Unavailable,
    Ended,
    Faulted,
}

// MmsysErr is the waveIn code of a failed open, 0 when the problem carries
// none. ExceptionType is the type name of the exception behind Faulted, empty
// otherwise; the message stays out, since it can quote a device's name.
internal readonly record struct MicrophonePreviewFailure(
    MicrophoneProblem Problem, uint MmsysErr = 0, string ExceptionType = "")
{
    // The name a log line carries for the problem. One spelling, here, so the
    // log and a filter on it agree.
    public string Cause => Problem switch
    {
        MicrophoneProblem.NotDetected => "not-detected",
        MicrophoneProblem.InUse       => "in-use",
        MicrophoneProblem.Unavailable => "unavailable",
        MicrophoneProblem.Ended       => "ended",
        _                             => "exception",
    };

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
