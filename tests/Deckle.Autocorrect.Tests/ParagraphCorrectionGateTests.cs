using System.Text.Json;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// When paragraph correction acts: its own switch, the Autocorrect switch, an
// app where autocorrect acts, a field that is not a password, and the local
// model available, all at once. Each condition alone turns it off.
[Trait("Category", "unit")]
public sealed class ParagraphCorrectionGateTests
{
    private static readonly FocusedSurface Editor =
        new("notepad", IsPassword: false, IsTextEditable: true);

    private static AutocorrectSettings Settings(
        bool paragraph = true, bool autocorrect = true, bool enrolled = true)
    {
        var settings = new AutocorrectSettings
        {
            Enabled = autocorrect,
            ParagraphCorrection = paragraph,
        };
        if (enrolled) settings.Apps["notepad"] = true;
        return settings;
    }

    [Fact]
    public void ActsWhenEveryConditionHolds()
    {
        Assert.True(ParagraphCorrectionGate.IsActive(Settings(), Editor, ollamaAvailable: true));
    }

    [Fact]
    public void TheOwnSwitchOffTurnsItOff()
    {
        Assert.False(ParagraphCorrectionGate.IsActive(Settings(paragraph: false), Editor, true));
        Assert.False(ParagraphCorrectionGate.IsActive(Settings(paragraph: false), null, true));
    }

    [Fact]
    public void TheAutocorrectSwitchOffTurnsItOff()
    {
        Assert.False(ParagraphCorrectionGate.IsActive(Settings(autocorrect: false), Editor, true));
        Assert.False(ParagraphCorrectionGate.IsActive(Settings(autocorrect: false), null, true));
    }

    [Fact]
    public void AnUnavailableOllamaOrModelTurnsItOff()
    {
        Assert.False(ParagraphCorrectionGate.IsActive(Settings(), Editor, ollamaAvailable: false));
        Assert.False(ParagraphCorrectionGate.IsActive(Settings(), null, ollamaAvailable: false));
    }

    [Fact]
    public void AnAppWhereAutocorrectDoesNotActTurnsItOff()
    {
        // Never met, and declined: both are apps autocorrect leaves alone.
        Assert.False(ParagraphCorrectionGate.IsActive(Settings(enrolled: false), Editor, true));

        AutocorrectSettings declined = Settings(enrolled: false);
        declined.Apps["notepad"] = false;
        Assert.False(ParagraphCorrectionGate.IsActive(declined, Editor, true));
    }

    [Fact]
    public void TheAppIsMatchedWithoutRegardToCase()
    {
        var surface = new FocusedSurface("NotePad", IsPassword: false, IsTextEditable: true);

        Assert.True(ParagraphCorrectionGate.IsActive(Settings(), surface, true));
    }

    [Fact]
    public void APasswordFieldTurnsItOff()
    {
        var password = Editor with { IsPassword = true };

        Assert.False(ParagraphCorrectionGate.IsActive(Settings(), password, true));
    }

    [Fact]
    public void ASurfaceThatIsNotTextEditableTurnsItOff()
    {
        Assert.False(ParagraphCorrectionGate.IsActive(Settings(), FocusedSurface.Unknown, true));
    }

    [Fact]
    public void WithoutAKnownSurfaceTheHostRunsWhereverAnAppMightBeEnrolled()
    {
        // The host is held before any focused app is known: the enrolled-app
        // condition belongs to the draft and the offer.
        Assert.True(ParagraphCorrectionGate.IsActive(Settings(enrolled: false), null, true));
    }

    [Fact]
    public void ASettingsFileWrittenBeforeTheSwitchExistedKeepsItOn()
    {
        var s = JsonSerializer.Deserialize<AutocorrectSettings>(
            """{"enabled":true,"apps":{"notepad":true}}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;

        Assert.True(s.ParagraphCorrection);
    }

    [Fact]
    public void TheEnginesPerAppDecisionIsTheSettingsOwn()
    {
        AutocorrectSettings s = Settings();

        Assert.True(s.IsEnabledFor("notepad"));
        Assert.False(s.IsEnabledFor("chrome"));
        Assert.False(s.IsEnabledFor(""));
    }
}
