using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class TaskbarDocumentBuilderTests : IDisposable
{
    private readonly NativeSelectorLibrary _selectors = new();

    public void Dispose() => _selectors.Dispose();

    private static TaskbarRule Rule(TaskbarCustomization customization, string id) =>
        TaskbarDocumentBuilder.Rules(customization).Single(rule => rule.Id == id);

    private static readonly TaskbarCustomization _everything = new()
    {
        Background = TaskbarBackground.Transparent,
        TrayIconPitch = 24,
        TightenQuickSettings = true,
        Clock = ClockCustomization.DateAboveTime,
    };

    [Fact]
    public void ACustomizationThatChangesNothingBuildsNoRulesAndNoBehaviors()
    {
        var customization = new TaskbarCustomization();

        Assert.Empty(TaskbarDocumentBuilder.Rules(customization));
        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.Build(customization));
        Assert.Equal(0, document.RootElement.GetProperty("rules").GetArrayLength());
        Assert.Empty(document.RootElement.GetProperty("behaviors").EnumerateObject());
    }

    [Fact]
    public void ATransparentBackgroundEmptiesTheFillAndTheEdgeLine()
    {
        var customization = new TaskbarCustomization { Background = TaskbarBackground.Transparent };

        Assert.Equal("Transparent", Rule(customization, "background.fill").Value);
        Assert.Equal("Transparent", Rule(customization, "background.stroke").Value);
        Assert.Equal("Fill", Rule(customization, "background.fill").Property);
    }

    [Fact]
    public void ABlackBackgroundFillsBlackAndStillEmptiesTheEdgeLine()
    {
        var customization = new TaskbarCustomization { Background = TaskbarBackground.Black };

        Assert.Equal("Black", Rule(customization, "background.fill").Value);
        Assert.Equal("Transparent", Rule(customization, "background.stroke").Value);
    }

    [Fact]
    public void ATrayPitchSetsTheHeightOfEachIconAndFreesItsWidth()
    {
        var customization = new TaskbarCustomization { TrayIconPitch = 24 };

        TaskbarRule height = Rule(customization, "tray.icon.minHeight");
        Assert.Equal(("MinHeight", "24"), (height.Property, height.Value));
        Assert.Equal(("MinWidth", "0"), (Rule(customization, "tray.icon.minWidth").Property, Rule(customization, "tray.icon.minWidth").Value));
        Assert.Equal(height.Target, Rule(customization, "tray.icon.minWidth").Target);
    }

    [Fact]
    public void TighteningTheQuickSettingsFollowsTheTrayPitchOrDefaultsTo24()
    {
        var followed = new TaskbarCustomization { TrayIconPitch = 28, TightenQuickSettings = true };
        var alone = new TaskbarCustomization { TightenQuickSettings = true };

        Assert.Equal("28", Rule(followed, "quickSettings.icon.height").Value);
        Assert.Equal("24", Rule(alone, "quickSettings.icon.height").Value);
        Assert.Equal("0", Rule(alone, "quickSettings.spacing").Value);
        Assert.DoesNotContain(TaskbarDocumentBuilder.Rules(alone), rule => rule.Id.StartsWith("tray.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheClockKeepsLouissWindhawkStyles()
    {
        var customization = new TaskbarCustomization { Clock = ClockCustomization.DateAboveTime };

        Assert.Equal("4", Rule(customization, "clock.spacing").Value);
        Assert.Equal("12", Rule(customization, "clock.upper.fontSize").Value);
        Assert.Equal("#C8C8C8", Rule(customization, "clock.upper.color").Value);
        Assert.Equal("18", Rule(customization, "clock.lower.fontSize").Value);
        Assert.Equal("Medium", Rule(customization, "clock.lower.fontWeight").Value);
        Assert.EndsWith("TextBlock#TimeInnerTextBlock", Rule(customization, "clock.upper.fontSize").Target);
        Assert.EndsWith("TextBlock#DateInnerTextBlock", Rule(customization, "clock.lower.fontSize").Target);

        // Only what the settings give a value to becomes a rule.
        Assert.DoesNotContain(TaskbarDocumentBuilder.Rules(customization), rule => rule.Id is "clock.upper.fontWeight" or "clock.lower.color");
    }

    [Fact]
    public void TheClockBehaviorNamesTheFiveLinesInEnglish()
    {
        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.Build(_everything));
        JsonElement clock = document.RootElement.GetProperty("behaviors").GetProperty("clock");

        Assert.True(clock.GetProperty("enabled").GetBoolean());
        Assert.Equal("en-US", clock.GetProperty("locale").GetString());
        Assert.Equal(
            ["date:MMM.", "date:dd", "date:ddd"],
            clock.GetProperty("upper").EnumerateArray().Select(Line).ToArray());
        Assert.Equal(
            ["time:HH", "time:mm"],
            clock.GetProperty("lower").EnumerateArray().Select(Line).ToArray());

        static string Line(JsonElement line)
        {
            JsonProperty only = line.EnumerateObject().Single();
            return only.Name + ":" + only.Value.GetString();
        }
    }

    [Fact]
    public void EveryRuleHasAUniqueIdAPropertyAndAValue()
    {
        var rules = TaskbarDocumentBuilder.Rules(_everything);

        Assert.Equal(rules.Count, rules.Select(rule => rule.Id).Distinct().Count());
        Assert.All(rules, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Property));
            Assert.False(string.IsNullOrWhiteSpace(rule.Value));
        });
    }

    [Fact]
    public void EveryTargetIsASelectorTheEnginesParserReads()
    {
        foreach (TaskbarRule rule in TaskbarDocumentBuilder.Rules(_everything))
        {
            string? parsed = _selectors.Describe(rule.Target);
            if (parsed is null) return;   // no DLL in this build
            Assert.False(parsed.StartsWith("error", StringComparison.Ordinal), $"{rule.Id}: {parsed}");
        }
    }

    [Fact]
    public void NumbersAreWrittenWithAPointWhateverTheCultureOfTheMachine()
    {
        var customization = new TaskbarCustomization
        {
            Clock = ClockCustomization.DateAboveTime with { UpperStyle = new ClockTextStyle(12.5) },
        };

        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.Equal("12.5", Rule(customization, "clock.upper.fontSize").Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheDocumentTravelsAsOneLineThroughTheApplyCommand()
    {
        string line = NativeCommand.Apply(TaskbarDocumentBuilder.Build(_everything));

        Assert.DoesNotContain('\n', line);
        using JsonDocument command = JsonDocument.Parse(line);
        Assert.Equal("apply", command.RootElement.GetProperty("t").GetString());
        Assert.Equal(11, command.RootElement.GetProperty("doc").GetProperty("rules").GetArrayLength());
    }
}
