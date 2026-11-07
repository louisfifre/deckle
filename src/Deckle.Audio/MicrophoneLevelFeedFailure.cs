namespace Deckle.Audio;

// Why a MicrophoneLevelFeed closed without being asked to. A failed open keeps
// the kind the pre-flight probe gives the same error code
// (MicrophoneCapture.ClassifyError); the last two are the capture ending, or
// throwing, with no error code to read. Each surface words it for its reader.
public enum MicrophoneProblem
{
    NotDetected,
    InUse,
    Unavailable,
    Ended,
    Faulted,
}

// ErrorCode is the HRESULT of a failed open (WASAPI or MMDevice), 0 when the
// problem carries none. ExceptionType is the type name of the exception behind Faulted, empty
// otherwise; the message stays out, since it can quote a device's name.
public readonly record struct MicrophoneLevelFeedFailure(
    MicrophoneProblem Problem, uint ErrorCode = 0, string ExceptionType = "")
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
}
