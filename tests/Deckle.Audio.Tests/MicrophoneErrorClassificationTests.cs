using Xunit;

namespace Deckle.Audio.Tests;

// A caller that reads a failed capture's code must read it as the probe does,
// so the codes the user can act on keep their kind.
public sealed class MicrophoneErrorClassificationTests
{
    [Theory]
    [Trait("Category", "unit")]
    [InlineData(0u, MicErrorKind.None)]
    [InlineData(0x80070490u, MicErrorKind.NotDetected)] // E_NOTFOUND: no such microphone
    [InlineData(0x88890004u, MicErrorKind.NotDetected)] // AUDCLNT_E_DEVICE_INVALIDATED
    [InlineData(0x8889000Au, MicErrorKind.InUse)]       // AUDCLNT_E_DEVICE_IN_USE
    [InlineData(0xFFFFD8F4u, MicErrorKind.NotDetected)] // paInvalidDevice (-9996) without a host error
    [InlineData(0x80004005u, MicErrorKind.Unavailable)] // any other code
    public void AnErrorCodeKeepsTheKindTheProbeGivesIt(uint code, MicErrorKind expected)
    {
        Assert.Equal(expected, MicrophoneCapture.ClassifyError(code));
    }
}
