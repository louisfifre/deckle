namespace Deckle.Audio.Internal;

// What an error code from the capture means to Deckle. A code is one of two
// kinds, both negative as an int:
//   - an HRESULT from the Windows audio stack (0x8889000A, AUDCLNT_E_DEVICE_IN_USE),
//     the precise one, which PortAudio records as its last host error when
//     Windows refuses a call;
//   - a PaError (-9996, paInvalidDevice) when PortAudio refused on its own or
//     reported no host error.
// The two ranges do not overlap: a PaError sits in [-10000, -9900], an HRESULT
// has its top bit set and is far below. The capture keeps the HRESULT when it
// has one, so the log and the message name what Windows said.
//
// Classify gives the three-way reading (not detected, in use, unavailable)
// the pipelines turn into a message; Name gives a symbol, so a reader of the
// log does not need the header.
internal static class CaptureErrors
{
    public const int E_ACCESSDENIED                   = unchecked((int)0x80070005);
    public const int E_INVALIDARG                     = unchecked((int)0x80070057);
    public const int E_NOTFOUND                       = unchecked((int)0x80070490);
    public const int E_OUTOFMEMORY                    = unchecked((int)0x8007000E);
    public const int E_FAIL                           = unchecked((int)0x80004005);
    public const int CO_E_NOTINITIALIZED              = unchecked((int)0x800401F0);
    public const int AUDCLNT_E_NOT_INITIALIZED        = unchecked((int)0x88890001);
    public const int AUDCLNT_E_DEVICE_INVALIDATED     = unchecked((int)0x88890004);
    public const int AUDCLNT_E_UNSUPPORTED_FORMAT     = unchecked((int)0x88890008);
    public const int AUDCLNT_E_DEVICE_IN_USE          = unchecked((int)0x8889000A);
    public const int AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED = unchecked((int)0x8889000E);
    public const int AUDCLNT_E_ENDPOINT_CREATE_FAILED = unchecked((int)0x8889000F);
    public const int AUDCLNT_E_SERVICE_NOT_RUNNING    = unchecked((int)0x88890010);
    public const int AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED = unchecked((int)0x88890019);
    public const int AUDCLNT_E_INVALID_DEVICE_PERIOD  = unchecked((int)0x88890020);
    public const int AUDCLNT_E_RESOURCES_INVALIDATED  = unchecked((int)0x88890026);
    public const int AUDCLNT_E_RAW_MODE_UNSUPPORTED   = unchecked((int)0x88890027);

    // The PortAudio range: PaErrorCode starts at -10000 and grows by one.
    private const int PaErrorFirst = -10000;
    private const int PaErrorLast  = -9900;

    public static bool IsPortAudioError(int code) => code is >= PaErrorFirst and <= PaErrorLast;

    public static MicErrorKind Classify(int code) => code switch
    {
        0 => MicErrorKind.None,

        // Another application holds the device in a mode that excludes Deckle.
        AUDCLNT_E_DEVICE_IN_USE or PortAudioInterop.DeviceUnavailable => MicErrorKind.InUse,

        // The device does not exist (any more): unplugged, disabled, or no
        // capture device at all. PortAudio's paInvalidDevice without a host
        // error is the index it could not resolve.
        E_NOTFOUND
            or AUDCLNT_E_DEVICE_INVALIDATED
            or AUDCLNT_E_ENDPOINT_CREATE_FAILED
            or PortAudioInterop.InvalidDevice => MicErrorKind.NotDetected,

        _ => MicErrorKind.Unavailable,
    };

    public static MicErrorKind Classify(uint code) => Classify(unchecked((int)code));

    // The code as the log and the message print it: an HRESULT in hex, as
    // Microsoft documents it (0x8889000A rather than 2290679626), a PaError in
    // decimal, as PortAudio's header lists it (-9996).
    public static string Format(int code) => IsPortAudioError(code)
        ? code.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "0x" + unchecked((uint)code).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    public static string Format(uint code) => Format(unchecked((int)code));

    public static string Name(int code) => code switch
    {
        0                                     => "OK",
        E_ACCESSDENIED                        => "E_ACCESSDENIED",
        E_INVALIDARG                          => "E_INVALIDARG",
        E_NOTFOUND                            => "E_NOTFOUND",
        E_OUTOFMEMORY                         => "E_OUTOFMEMORY",
        E_FAIL                                => "E_FAIL",
        CO_E_NOTINITIALIZED                   => "CO_E_NOTINITIALIZED",
        AUDCLNT_E_NOT_INITIALIZED             => "AUDCLNT_E_NOT_INITIALIZED",
        AUDCLNT_E_DEVICE_INVALIDATED          => "AUDCLNT_E_DEVICE_INVALIDATED",
        AUDCLNT_E_UNSUPPORTED_FORMAT          => "AUDCLNT_E_UNSUPPORTED_FORMAT",
        AUDCLNT_E_DEVICE_IN_USE               => "AUDCLNT_E_DEVICE_IN_USE",
        AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED  => "AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED",
        AUDCLNT_E_ENDPOINT_CREATE_FAILED      => "AUDCLNT_E_ENDPOINT_CREATE_FAILED",
        AUDCLNT_E_SERVICE_NOT_RUNNING         => "AUDCLNT_E_SERVICE_NOT_RUNNING",
        AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED     => "AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED",
        AUDCLNT_E_INVALID_DEVICE_PERIOD       => "AUDCLNT_E_INVALID_DEVICE_PERIOD",
        AUDCLNT_E_RESOURCES_INVALIDATED       => "AUDCLNT_E_RESOURCES_INVALIDATED",
        AUDCLNT_E_RAW_MODE_UNSUPPORTED        => "AUDCLNT_E_RAW_MODE_UNSUPPORTED",
        PortAudioInterop.NotInitialized       => "paNotInitialized",
        PortAudioInterop.UnanticipatedHostError => "paUnanticipatedHostError",
        PortAudioInterop.InvalidChannelCount  => "paInvalidChannelCount",
        PortAudioInterop.InvalidSampleRate    => "paInvalidSampleRate",
        PortAudioInterop.InvalidDevice        => "paInvalidDevice",
        PortAudioInterop.InvalidFlag          => "paInvalidFlag",
        PortAudioInterop.SampleFormatNotSupported => "paSampleFormatNotSupported",
        PortAudioInterop.InsufficientMemory   => "paInsufficientMemory",
        PortAudioInterop.BadStreamPtr         => "paBadStreamPtr",
        PortAudioInterop.TimedOut             => "paTimedOut",
        PortAudioInterop.InternalError        => "paInternalError",
        PortAudioInterop.DeviceUnavailable    => "paDeviceUnavailable",
        PortAudioInterop.IncompatibleHostApiSpecificStreamInfo => "paIncompatibleHostApiSpecificStreamInfo",
        PortAudioInterop.StreamIsStopped      => "paStreamIsStopped",
        PortAudioInterop.StreamIsNotStopped   => "paStreamIsNotStopped",
        _ when IsPortAudioError(code)         => "paError",
        _                                     => "HRESULT_UNKNOWN",
    };

    public static string Name(uint code) => Name(unchecked((int)code));

    // The failures after which asking the same device in another mode cannot
    // help: it is not there, access is refused, or the audio service is down.
    // A device held by another application ends the shared attempts too, but
    // not an exclusive one: exclusive mode is refused by any application using
    // the device, and sharing it then still works.
    public static bool EndsFallback(int code, bool exclusiveAttempt) => code switch
    {
        E_NOTFOUND or E_ACCESSDENIED
            or AUDCLNT_E_DEVICE_INVALIDATED
            or AUDCLNT_E_SERVICE_NOT_RUNNING
            or AUDCLNT_E_ENDPOINT_CREATE_FAILED => true,
        AUDCLNT_E_DEVICE_IN_USE => !exclusiveAttempt,
        _ => false,
    };
}
