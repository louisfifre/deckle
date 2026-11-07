namespace Deckle.Audio.Internal;

// A capture endpoint as the microphone picker shows it: the MMDevice id the
// settings keep, and the friendly name a person reads.
internal readonly record struct MicrophoneEndpoint(string Id, string Name);

// The microphones Windows currently offers, from the same enumerator the
// capture opens them with, so a device the picker lists is a device the
// capture can ask for by that id.
internal static class MicrophoneEndpointCatalog
{
    // Active capture endpoints, in the order Windows lists them. Empty when none
    // is plugged in, or when the audio stack could not be reached.
    public static IReadOnlyList<MicrophoneEndpoint> Active()
    {
        using var apartment = ComApartment.Enter();
        if (WasapiInterop.CreateDeviceEnumerator(out nint enumerator) < 0) return [];

        nint collection = 0;
        try
        {
            if (WasapiInterop.EnumerateEndpoints(
                    enumerator, WasapiInterop.eCapture, WasapiInterop.DEVICE_STATE_ACTIVE, out collection) < 0
                || WasapiInterop.CollectionCount(collection, out uint count) < 0)
                return [];

            var endpoints = new List<MicrophoneEndpoint>((int)count);
            for (uint i = 0; i < count; i++)
            {
                if (WasapiInterop.CollectionItem(collection, i, out nint device) < 0) continue;
                try
                {
                    if (WasapiInterop.DeviceId(device, out string id) < 0 || id.Length == 0) continue;
                    endpoints.Add(new MicrophoneEndpoint(id, NameOf(device)));
                }
                finally { WasapiInterop.Release(device); }
            }
            return endpoints;
        }
        finally
        {
            WasapiInterop.Release(collection);
            WasapiInterop.Release(enumerator);
        }
    }

    private static string NameOf(nint device)
    {
        if (WasapiInterop.DeviceOpenPropertyStore(device, out nint store) < 0)
            return CaptureDeviceIdentity.UnknownName;
        try
        {
            string? name = WasapiInterop.PropertyString(store, WasapiInterop.PKEY_Device_FriendlyName);
            return string.IsNullOrWhiteSpace(name) ? CaptureDeviceIdentity.UnknownName : name.Trim();
        }
        finally { WasapiInterop.Release(store); }
    }
}
