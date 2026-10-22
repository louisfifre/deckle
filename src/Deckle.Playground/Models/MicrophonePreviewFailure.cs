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
    // journal and a filter on it agree.
    public string Cause => Problem switch
    {
        MicrophoneProblem.NotDetected => "not-detected",
        MicrophoneProblem.InUse       => "in-use",
        MicrophoneProblem.Unavailable => "unavailable",
        MicrophoneProblem.Ended       => "ended",
        _                             => "exception",
    };
}
