using System.Runtime.InteropServices;

namespace Deckle.Audio.Internal;

// Turns the microphone choice the waveIn capture saved (a device index) into
// the MMDevice endpoint id the WASAPI capture opens, once, when the settings
// file is migrated.
//
// Why an index could not simply be kept: a waveIn index is a position in the
// list the driver enumerates now, so it names a different microphone as soon as
// one is plugged or unplugged. The endpoint id names the device itself.
//
// How the index is resolved. winmm answers DRV_QUERYFUNCTIONINSTANCEID for a
// device index with the id of the endpoint behind it, the same device the
// index opened on that machine; no name is compared (waveIn names are cut at
// 31 characters, so two similar devices can collide). On Windows 10 and 11 the
// answer is the endpoint id itself ({0.0.1.00000000}.{guid}), and older
// drivers answer a device interface path that contains it.
internal static class LegacyWaveInDeviceMap
{
    // mmddk.h: DRV_RESERVED (0x0800) + 17 and + 18.
    private const uint DRV_QUERYFUNCTIONINSTANCEID = 0x0811;
    private const uint DRV_QUERYFUNCTIONINSTANCEIDSIZE = 0x0812;

    [DllImport("winmm.dll")]
    private static extern uint waveInMessage(nint hwi, uint uMsg, nint dw1, nint dw2);

    // The endpoint id of waveIn device `index`, or null when the index names no
    // device any more or the driver does not answer.
    public static unsafe string? EndpointIdOf(int index)
    {
        if (index < 0) return null;

        uint bytes = 0;
        if (waveInMessage(index, DRV_QUERYFUNCTIONINSTANCEIDSIZE, (nint)(&bytes), 0) != 0 || bytes == 0)
            return null;

        nint buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (waveInMessage(index, DRV_QUERYFUNCTIONINSTANCEID, buffer, (nint)bytes) != 0)
                return null;
            return ParseEndpointId(Marshal.PtrToStringUni(buffer));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // The endpoint id inside what the driver answered, or null when there is
    // none. Accepts the bare id, or a device interface path such as
    // \\?\SWD#MMDEVAPI#{0.0.1.00000000}.{guid}#{2eef81be-...}.
    public static string? ParseEndpointId(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        answer = answer.Trim();

        const string Marker = "MMDEVAPI#";
        int marker = answer.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            int start = marker + Marker.Length;
            int end = answer.IndexOf('#', start);
            answer = end < 0 ? answer[start..] : answer[start..end];
        }

        // {flow.state.index.hex}.{device guid}: both halves in braces.
        return answer.StartsWith('{') && answer.Contains("}.{", StringComparison.Ordinal)
            ? answer
            : null;
    }
}
