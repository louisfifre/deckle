using Deckle.App.SelfCheck;
using Deckle.Audio;
using Deckle.Hosting.SelfCheck;
using Xunit;

namespace Deckle.App.Tests;

// What stays in the host of the self-check: the pieces that need the host's own
// modules. The gate, the verdict, the target and the images are the shared
// harness's and are tested with it (Deckle.Hosting.Tests); the mode that opens
// real windows is run by scripts/self-check.ps1, not here.
public class SelfCheckTests
{
    // portaudio.dll beside the wrong folder: the library did not load. The
    // stage decides first, because that failure carries the same code (-10000)
    // a refusing device can.
    [Fact]
    [Trait("Category", "regression")]
    public void Microphone_ALibraryThatDidNotLoadIsNotTakenForARefusingDevice()
    {
        SelfCheckMicrophone outcome = SelfCheckMicrophoneOutcome.From(
            ok: false, stage: "load", MicErrorKind.Unavailable, "paNotInitialized", "-10000");

        Assert.Equal(SelfCheckMicrophone.LoadFailed, outcome.Outcome);
    }

    [Theory]
    [InlineData("initialize", MicErrorKind.Unavailable, SelfCheckMicrophone.InitializeFailed)]
    [InlineData("resolve", MicErrorKind.NotDetected, SelfCheckMicrophone.DeviceMissing)]
    [InlineData("open", MicErrorKind.InUse, SelfCheckMicrophone.DeviceBusy)]
    [InlineData("open", MicErrorKind.Unavailable, SelfCheckMicrophone.DeviceRefused)]
    [Trait("Category", "unit")]
    public void Microphone_IsClassifiedByStageThenByKind(string stage, MicErrorKind kind, string expected)
    {
        SelfCheckMicrophone outcome = SelfCheckMicrophoneOutcome.From(false, stage, kind, "E", "0");

        Assert.Equal(expected, outcome.Outcome);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Microphone_AnOpenProbeIsOk()
    {
        Assert.Equal(SelfCheckMicrophone.Ok,
            SelfCheckMicrophoneOutcome.From(true, null, MicErrorKind.Unavailable, "", "").Outcome);
    }

    // The published run walks the host's own windows around the contributed
    // ones, Projects last, and Projects shows several views.
    [Fact]
    [Trait("Category", "unit")]
    public void Catalog_WalksTheHostWindowsThenTheContributedOnesThenProjects()
    {
        SelfCheckWindows catalog = SelfCheckWindowCatalog.Published;

        Assert.Equal(["settings", "log", "transcribe-rewrite", "paste-to-folder"], catalog.Windows.Take(4));
        Assert.Equal("projects", catalog.Windows[^1]);
        Assert.Contains("projects", catalog.WithViews);
        Assert.DoesNotContain("log", catalog.WithViews);
    }
}
