namespace Deckle.Audio.Internal;

// What an HRESULT from the audio stack means to Deckle: a symbolic name so a
// reader of the log does not need the header to tell "in use" from "device
// invalidated", and the three-way reading (not detected, in use, unavailable)
// the pipelines turn into a message.
internal static class WasapiErrors
{
    public static MicErrorKind Classify(uint hresult) => unchecked((int)hresult) switch
    {
        0 => MicErrorKind.None,

        // Another application holds the device in a mode that excludes Deckle.
        WasapiInterop.AUDCLNT_E_DEVICE_IN_USE => MicErrorKind.InUse,

        // The endpoint does not exist (any more): unplugged, disabled, or no
        // default capture device at all.
        WasapiInterop.E_NOTFOUND
            or WasapiInterop.AUDCLNT_E_DEVICE_INVALIDATED
            or WasapiInterop.AUDCLNT_E_ENDPOINT_CREATE_FAILED => MicErrorKind.NotDetected,

        _ => MicErrorKind.Unavailable,
    };

    // The HRESULT as the log and the message print it: 0x8889000A.
    public static string Hex(uint hresult) => "0x" + hresult.ToString("X8");

    public static string Name(uint hresult) => unchecked((int)hresult) switch
    {
        0                                               => "S_OK",
        WasapiInterop.E_NOINTERFACE                     => "E_NOINTERFACE",
        WasapiInterop.E_FAIL                            => "E_FAIL",
        WasapiInterop.E_ACCESSDENIED                    => "E_ACCESSDENIED",
        WasapiInterop.E_INVALIDARG                      => "E_INVALIDARG",
        WasapiInterop.E_NOTFOUND                        => "E_NOTFOUND",
        unchecked((int)0x80040154)                      => "REGDB_E_CLASSNOTREG",
        unchecked((int)0x88890001)                      => "AUDCLNT_E_NOT_INITIALIZED",
        unchecked((int)0x88890002)                      => "AUDCLNT_E_ALREADY_INITIALIZED",
        WasapiInterop.AUDCLNT_E_DEVICE_INVALIDATED      => "AUDCLNT_E_DEVICE_INVALIDATED",
        WasapiInterop.AUDCLNT_E_UNSUPPORTED_FORMAT      => "AUDCLNT_E_UNSUPPORTED_FORMAT",
        WasapiInterop.AUDCLNT_E_DEVICE_IN_USE           => "AUDCLNT_E_DEVICE_IN_USE",
        unchecked((int)0x8889000E)                      => "AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED",
        WasapiInterop.AUDCLNT_E_ENDPOINT_CREATE_FAILED  => "AUDCLNT_E_ENDPOINT_CREATE_FAILED",
        WasapiInterop.AUDCLNT_E_SERVICE_NOT_RUNNING     => "AUDCLNT_E_SERVICE_NOT_RUNNING",
        unchecked((int)0x88890020)                      => "AUDCLNT_E_INVALID_DEVICE_PERIOD",
        WasapiInterop.AUDCLNT_E_RESOURCES_INVALIDATED   => "AUDCLNT_E_RESOURCES_INVALIDATED",
        WasapiInterop.AUDCLNT_E_RAW_MODE_UNSUPPORTED    => "AUDCLNT_E_RAW_MODE_UNSUPPORTED",
        _                                               => "HRESULT_UNKNOWN",
    };

    // The failures after which trying the same endpoint in the default mode
    // cannot help: the device is not there, is held, or the audio service is
    // down. Every other refusal of a raw stream (unsupported raw mode, a
    // refused property, a rejected format) is worth one more try without raw.
    public static bool IsDeviceLevel(int hresult) => hresult
        is WasapiInterop.E_NOTFOUND
        or WasapiInterop.E_ACCESSDENIED
        or WasapiInterop.AUDCLNT_E_DEVICE_INVALIDATED
        or WasapiInterop.AUDCLNT_E_DEVICE_IN_USE
        or WasapiInterop.AUDCLNT_E_SERVICE_NOT_RUNNING
        or WasapiInterop.AUDCLNT_E_ENDPOINT_CREATE_FAILED;
}
