using Xunit;

namespace Deckle.Audio.Tests;

// A caller that reads a failed capture's code must read it as the probe does,
// so the codes the user can act on keep their kind.
public sealed class MicrophoneErrorClassificationTests
{
    [Theory]
    [Trait("Category", "unit")]
    [InlineData(0u, MicErrorKind.None)]
    [InlineData(2u, MicErrorKind.NotDetected)] // MMSYSERR_BADDEVICEID
    [InlineData(6u, MicErrorKind.NotDetected)] // MMSYSERR_NODRIVER
    [InlineData(4u, MicErrorKind.InUse)]       // MMSYSERR_ALLOCATED
    [InlineData(7u, MicErrorKind.Unavailable)] // any other code
    public void AnErrorCodeKeepsTheKindTheProbeGivesIt(uint code, MicErrorKind expected)
    {
        Assert.Equal(expected, MicrophoneCapture.ClassifyError(code));
    }
}
