using Deckle.App.SelfCheck;
using Deckle.Audio;
using Xunit;

namespace Deckle.App.Tests;

// Pins the rules of the self-check that decide whether the maintainer's data is
// safe and whether a run passes: the data-root gate, the verdict of the report,
// the reading of the run's log and the microphone classification. The mode that
// opens real windows is run by scripts/self-check.ps1, not here.
public class SelfCheckTests
{
    private const string DefaultRoot = @"C:\Users\someone\AppData\Local\Deckle";

    // ── The data-root gate ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "unit")]
    public void Gate_RefusesWhenTheVariableIsNotSet(string? variable)
    {
        SelfCheckGateResult result = SelfCheckDataRootGate.Evaluate(variable, DefaultRoot);

        Assert.False(result.Allowed);
        Assert.Contains("not set", result.Reason);
    }

    [Theory]
    [InlineData(@"C:\Users\someone\AppData\Local\Deckle")]
    [InlineData(@"C:\Users\someone\AppData\Local\Deckle\")]
    [InlineData(@"c:\users\SOMEONE\appdata\local\deckle")]
    [InlineData(@"C:\Users\someone\AppData\Local\Other\..\Deckle")]
    [Trait("Category", "unit")]
    public void Gate_RefusesTheDefaultRootWhateverItsSpelling(string variable)
    {
        SelfCheckGateResult result = SelfCheckDataRootGate.Evaluate(variable, DefaultRoot);

        Assert.False(result.Allowed);
        Assert.Contains("default data root", result.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Gate_RefusesAFolderInsideTheDefaultRoot()
    {
        SelfCheckGateResult result = SelfCheckDataRootGate.Evaluate(DefaultRoot + @"\self-check", DefaultRoot);

        Assert.False(result.Allowed);
        Assert.Contains("inside", result.Reason);
    }

    [Theory]
    [InlineData(@"C:\Temp\deckle-self-check\run1\data")]
    // A sibling whose name starts like the default root is not inside it.
    [InlineData(@"C:\Users\someone\AppData\Local\Deckle-self-check")]
    [Trait("Category", "unit")]
    public void Gate_AllowsARootOfItsOwn(string variable)
    {
        Assert.True(SelfCheckDataRootGate.Evaluate(variable, DefaultRoot).Allowed);
    }

    // ── The verdict ──────────────────────────────────────────────────────────

    private static SelfCheckReport PassingReport()
    {
        var report = new SelfCheckReport { Completed = true };
        report.Surfaces.Add(new SelfCheckSurface("settings", "general", "General", true, 300, @"captures\settings-general.png"));
        report.Surfaces.Add(new SelfCheckSurface("log", null, null, true, 400, @"captures\log.png"));
        report.Surfaces.Add(new SelfCheckSurface("playground", null, null, true, 400, @"captures\playground.png"));
        report.Microphone = new SelfCheckMicrophone(SelfCheckMicrophone.Ok, null, "OK", "0");
        return report;
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Verdict_PassesWhenEverythingLoadedAndTheMicrophoneIsFine()
    {
        SelfCheckReport report = PassingReport();

        report.Judge();

        Assert.Equal("passed", report.Outcome);
        Assert.Empty(report.Failures);
    }

    // The overflow of 0.87.14 killed the process on one page: what is left on
    // disk is a report that did not complete, with the page it was opening.
    [Fact]
    [Trait("Category", "regression")]
    public void Verdict_FailsAndNamesThePageWhenTheRunDidNotComplete()
    {
        SelfCheckReport report = PassingReport();
        report.Completed = false;
        report.Current = "settings/diagnostics (Diagnostics)";

        report.Judge();

        Assert.Equal("failed", report.Outcome);
        Assert.Contains(report.Failures, f => f.Contains("settings/diagnostics"));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Verdict_FailsWhenAPageDidNotComplete()
    {
        SelfCheckReport report = PassingReport();
        report.Surfaces.Add(new SelfCheckSurface("settings", "ambient", "Ambient", false, 3000, null));

        report.Judge();

        Assert.Equal("failed", report.Outcome);
        Assert.Contains(report.Failures, f => f.StartsWith("settings/ambient"));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Verdict_FailsWhenAPageLoadedButNoImageWasTaken()
    {
        SelfCheckReport report = PassingReport();
        report.Surfaces[0] = report.Surfaces[0] with { Png = null };

        report.Judge();

        Assert.Equal("failed", report.Outcome);
    }

    // The Autocorrect group's flyout stayed open over the Diagnostics image.
    [Fact]
    [Trait("Category", "regression")]
    public void Verdict_FailsWhenAnImageWasTakenWithAPopupOpen()
    {
        SelfCheckReport report = PassingReport();
        report.Surfaces[0] = report.Surfaces[0] with { PopupsClosed = 1, PopupsStillOpen = 1 };

        report.Judge();

        Assert.Equal("failed", report.Outcome);
        Assert.Contains(report.Failures, f => f.Contains("popup"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void Verdict_AcceptsAPopupThatWasClosedBeforeTheImage()
    {
        SelfCheckReport report = PassingReport();
        report.Surfaces[0] = report.Surfaces[0] with { PopupsClosed = 1 };

        report.Judge();

        Assert.Equal("passed", report.Outcome);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("log")]
    [InlineData("playground")]
    [Trait("Category", "unit")]
    public void Verdict_FailsWhenARequiredWindowWasNeverWalked(string window)
    {
        SelfCheckReport report = PassingReport();
        report.Surfaces.RemoveAll(s => s.Window == window);

        report.Judge();

        Assert.Equal("failed", report.Outcome);
        Assert.Contains(report.Failures, f => f.StartsWith(window));
    }

    // portaudio.dll beside the wrong folder: the library did not load.
    [Fact]
    [Trait("Category", "regression")]
    public void Verdict_FailsWhenTheNativeMicrophoneLibraryDidNotLoad()
    {
        SelfCheckReport report = PassingReport();
        report.Microphone = SelfCheckMicrophone.From(
            ok: false, stage: "load", MicErrorKind.Unavailable, "paNotInitialized", "-10000");

        report.Judge();

        Assert.Equal("failed", report.Outcome);
        Assert.Contains(report.Failures, f => f.Contains(SelfCheckMicrophone.LoadFailed));
    }

    [Theory]
    [InlineData("resolve", MicErrorKind.NotDetected, SelfCheckMicrophone.DeviceMissing)]
    [InlineData("open", MicErrorKind.InUse, SelfCheckMicrophone.DeviceBusy)]
    [InlineData("open", MicErrorKind.Unavailable, SelfCheckMicrophone.DeviceRefused)]
    [Trait("Category", "unit")]
    public void Verdict_OnlyWarnsWhenTheDeviceIsMissingBusyOrRefusing(
        string stage, MicErrorKind kind, string expectedOutcome)
    {
        SelfCheckReport report = PassingReport();
        report.Microphone = SelfCheckMicrophone.From(false, stage, kind, "E_NOTFOUND", "0x80070490");

        report.Judge();

        Assert.Equal(expectedOutcome, report.Microphone.Outcome);
        Assert.Equal("passed", report.Outcome);
        Assert.Single(report.Warnings);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Verdict_FailsWhenPortAudioCouldNotInitialize()
    {
        SelfCheckReport report = PassingReport();
        report.Microphone = SelfCheckMicrophone.From(false, "initialize", MicErrorKind.Unavailable, "paInternalError", "-9999");

        report.Judge();

        Assert.Equal("failed", report.Outcome);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Verdict_FailsOnALoggedError()
    {
        SelfCheckReport report = PassingReport();
        report.Errors.Add(new SelfCheckLogEvent("t", "Error", "Deckle-Settings", "NavFailedThrew", "boom"));

        report.Judge();

        Assert.Equal("failed", report.Outcome);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Report_SurvivesAJsonRoundTrip()
    {
        SelfCheckReport report = PassingReport();
        report.Current = "settings/home";

        SelfCheckReport? read = SelfCheckReport.Parse(report.ToJson());

        Assert.NotNull(read);
        Assert.Equal(3, read.Surfaces.Count);
        Assert.Equal("passed", read.Outcome);
        Assert.Equal("settings/home", read.Current);
    }

    // ── The images ───────────────────────────────────────────────────────────

    // RenderTargetBitmap hands over premultiplied BGRA with a transparent page
    // background: the image must come out opaque, on the surface's colour.
    [Fact]
    [Trait("Category", "unit")]
    public void Flatten_PutsTransparentPixelsOnTheSurfaceAndKeepsOpaqueOnes()
    {
        var surface = Windows.UI.Color.FromArgb(255, 0xF9, 0xF9, 0xF9);
        byte[] bgra =
        [
            0, 0, 0, 0,          // transparent
            10, 20, 30, 255,     // opaque, kept
        ];

        SelfCheck.ElementImage.FlattenOnto(bgra, surface);

        Assert.Equal(new byte[] { 0xF9, 0xF9, 0xF9, 255, 10, 20, 30, 255 }, bgra);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Flatten_AddsTheSurfaceInProportionToWhatTheAlphaLetsThrough()
    {
        var surface = Windows.UI.Color.FromArgb(255, 200, 200, 200);
        // Premultiplied half-covered black: colour 0, alpha 128.
        byte[] bgra = [0, 0, 0, 128];

        SelfCheck.ElementImage.FlattenOnto(bgra, surface);

        Assert.Equal(200 * (255 - 128) / 255, bgra[0]);
        Assert.Equal(255, bgra[3]);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Verdict_FailsWhenAPageRenderedNothingAndSaysWhy()
    {
        SelfCheckReport report = PassingReport();
        report.Surfaces[0] = report.Surfaces[0] with { Png = null, Error = "InvalidOperationException: empty" };

        report.Judge();

        Assert.Equal("failed", report.Outcome);
        Assert.Contains(report.Failures, f => f.Contains("empty"));
    }

    // ── The run's log ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "unit")]
    public void LogReader_KeepsOnlyErrorAndCriticalLines()
    {
        string[] lines =
        [
            """{"timestamp":"t1","level":"Warning","provider":"Deckle-App","event":"A","message":"w"}""",
            """{"timestamp":"t2","level":"Error","provider":"Deckle-Audio","event":"MicrophoneOpenFailed","message":"first\nsecond"}""",
            """{"timestamp":"t3","level":"Critical","provider":"Deckle-App","event":"CrashAppDomain","message":"c"}""",
            """{"timestamp":"t4","level":"Informational","provider":"Deckle-App","event":"B","message":"i"}""",
            "{ this line was cut by a crash",
            "",
        ];

        List<SelfCheckLogEvent> found = SelfCheckLogReader.ReadSevere(lines);

        Assert.Equal(["MicrophoneOpenFailed", "CrashAppDomain"], found.Select(e => e.Event));
        Assert.Equal("first", found[0].Message);
    }
}
