namespace Deckle.Audio.Internal;

// A capture endpoint as the microphone picker shows it: the MMDevice id the
// settings keep, and the friendly name a person reads.
internal readonly record struct MicrophoneEndpoint(string Id, string Name);

// The microphones Windows currently offers, from the same PortAudio list the
// capture opens them from, so a device the picker lists is a device the
// capture can ask for by that id.
internal static class MicrophoneEndpointCatalog
{
    // Active microphones, in the order PortAudio lists them. Empty when none is
    // plugged in, or when portaudio.dll could not be loaded.
    public static IReadOnlyList<MicrophoneEndpoint> Active()
    {
        try
        {
            return PortAudioHost.Invoke(() =>
            {
                IReadOnlyList<PortAudioInputDevice> inputs = PortAudioHost.ListInputs();
                var endpoints = new List<MicrophoneEndpoint>(inputs.Count);
                foreach (PortAudioInputDevice input in inputs)
                    endpoints.Add(new MicrophoneEndpoint(input.EndpointId, input.Name));
                return (IReadOnlyList<MicrophoneEndpoint>)endpoints;
            });
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return [];
        }
    }
}
