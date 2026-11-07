using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// Pins how a capture error code is printed, read and weighed: HRESULTs in hex,
// PortAudio errors in decimal, the three-way classification, and which
// failures end the walk down the fallback modes.
public class CaptureErrorsTests
{
    [Fact]
    [Trait("Category", "unit")]
    public void Format_PrintsAnHresultInHexAndAPortAudioErrorInDecimal()
    {
        Assert.Equal("0x8889000A", CaptureErrors.Format(CaptureErrors.AUDCLNT_E_DEVICE_IN_USE));
        Assert.Equal("0x8889000A", CaptureErrors.Format(0x8889000Au));
        Assert.Equal("-9996", CaptureErrors.Format(PortAudioInterop.InvalidDevice));
    }

    [Theory]
    [InlineData(0, MicErrorKind.None)]
    [InlineData(unchecked((int)0x8889000A), MicErrorKind.InUse)]
    [InlineData(PortAudioInterop.DeviceUnavailable, MicErrorKind.InUse)]
    [InlineData(unchecked((int)0x80070490), MicErrorKind.NotDetected)]
    [InlineData(unchecked((int)0x88890004), MicErrorKind.NotDetected)]
    [InlineData(unchecked((int)0x8889000F), MicErrorKind.NotDetected)]
    [InlineData(PortAudioInterop.InvalidDevice, MicErrorKind.NotDetected)]
    [InlineData(unchecked((int)0x80004005), MicErrorKind.Unavailable)]
    [InlineData(PortAudioInterop.InternalError, MicErrorKind.Unavailable)]
    [Trait("Category", "unit")]
    public void Classify_MapsACodeToItsKind(int code, MicErrorKind expected)
        => Assert.Equal(expected, CaptureErrors.Classify(code));

    [Fact]
    [Trait("Category", "unit")]
    public void Name_GivesTheSymbolOfAKnownCode()
    {
        Assert.Equal("AUDCLNT_E_DEVICE_IN_USE", CaptureErrors.Name(CaptureErrors.AUDCLNT_E_DEVICE_IN_USE));
        Assert.Equal("paInvalidDevice", CaptureErrors.Name(PortAudioInterop.InvalidDevice));
        Assert.Equal("OK", CaptureErrors.Name(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "unit")]
    public void EndsFallback_DeviceNotFound_EndsAnyAttempt(bool exclusiveAttempt)
        => Assert.True(CaptureErrors.EndsFallback(CaptureErrors.E_NOTFOUND, exclusiveAttempt));

    [Fact]
    [Trait("Category", "unit")]
    public void EndsFallback_DeviceInUse_EndsASharedAttemptButNotAnExclusiveOne()
    {
        Assert.True(CaptureErrors.EndsFallback(CaptureErrors.AUDCLNT_E_DEVICE_IN_USE, exclusiveAttempt: false));
        Assert.False(CaptureErrors.EndsFallback(CaptureErrors.AUDCLNT_E_DEVICE_IN_USE, exclusiveAttempt: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "unit")]
    public void EndsFallback_RawModeUnsupported_NeverEnds(bool exclusiveAttempt)
        => Assert.False(CaptureErrors.EndsFallback(CaptureErrors.AUDCLNT_E_RAW_MODE_UNSUPPORTED, exclusiveAttempt));
}
