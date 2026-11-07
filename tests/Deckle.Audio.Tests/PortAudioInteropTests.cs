using System.Runtime.InteropServices;
using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// Pins the managed mirrors of the PortAudio C structs (x64, pinned commit):
// one byte of drift from the DLL and PortAudio refuses the open or reads
// garbage, so sizes and field offsets are asserted against the C headers.
public unsafe class PortAudioInteropTests
{
    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    [Fact]
    [Trait("Category", "unit")]
    public void PaWasapiStreamInfo_MatchesTheHeaderLayout()
    {
        Assert.Equal(72, sizeof(PortAudioInterop.PaWasapiStreamInfo));
        Assert.Equal(24, Offset<PortAudioInterop.PaWasapiStreamInfo>(nameof(PortAudioInterop.PaWasapiStreamInfo.HostProcessorOutput)));
        Assert.Equal(40, Offset<PortAudioInterop.PaWasapiStreamInfo>(nameof(PortAudioInterop.PaWasapiStreamInfo.ThreadPriority)));
        Assert.Equal(48, Offset<PortAudioInterop.PaWasapiStreamInfo>(nameof(PortAudioInterop.PaWasapiStreamInfo.StreamOption)));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PaWasapiStreamInfo_Create_FillsSizeHostApiAndVersion()
    {
        var info = PortAudioInterop.PaWasapiStreamInfo.Create(PortAudioInterop.WasapiFlagExclusive, PortAudioInterop.WasapiStreamOptionRaw);

        Assert.Equal(72u, info.Size);
        Assert.Equal(13, info.HostApiType);
        Assert.Equal(1u, info.Version);
        Assert.Equal(PortAudioInterop.WasapiFlagExclusive, info.Flags);
        Assert.Equal(PortAudioInterop.WasapiStreamOptionRaw, info.StreamOption);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PaStreamParameters_MatchesTheHeaderLayout()
    {
        Assert.Equal(32, sizeof(PortAudioInterop.PaStreamParameters));
        Assert.Equal(24, Offset<PortAudioInterop.PaStreamParameters>(nameof(PortAudioInterop.PaStreamParameters.HostApiSpecificStreamInfo)));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PaDeviceInfo_MatchesTheHeaderLayout()
    {
        Assert.Equal(72, sizeof(PortAudioInterop.PaDeviceInfo));
        Assert.Equal(64, Offset<PortAudioInterop.PaDeviceInfo>(nameof(PortAudioInterop.PaDeviceInfo.DefaultSampleRate)));
    }
}
