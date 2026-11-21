using Deckle.Audio;
using Deckle.Hosting.SelfCheck;

namespace Deckle.App.SelfCheck;

// Turns the microphone probe's answer into the report's record: the one piece
// of the self-check that needs Deckle.Audio, so it stays in the host that
// probes a microphone and not in the shared harness.
internal static class SelfCheckMicrophoneOutcome
{
    // From a probe: Ok, Stage, the classified kind, the code and its symbol.
    // The stage decides first, because a library that did not load carries the
    // same code (-10000) a refusing device can.
    public static SelfCheckMicrophone From(
        bool ok, string? stage, MicErrorKind kind, string errorName, string errorCode)
    {
        if (ok) return new(SelfCheckMicrophone.Ok, null, "OK", "0");

        string outcome = stage switch
        {
            "load" => SelfCheckMicrophone.LoadFailed,
            "initialize" => SelfCheckMicrophone.InitializeFailed,
            _ => kind switch
            {
                MicErrorKind.NotDetected => SelfCheckMicrophone.DeviceMissing,
                MicErrorKind.InUse => SelfCheckMicrophone.DeviceBusy,
                _ => SelfCheckMicrophone.DeviceRefused,
            },
        };
        return new(outcome, stage, errorName, errorCode);
    }
}
