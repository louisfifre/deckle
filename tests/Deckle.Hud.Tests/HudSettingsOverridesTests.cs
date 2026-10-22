using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deckle.Composition;
using Xunit;
using static Deckle.Composition.HudComposition;

namespace Deckle.Hud.Tests;

// The HUD settings file holds only what differs from the compiled defaults.
// These tests pin that contract on the pure override logic, with the same
// serializer options HudSettingsService uses, and never touch the disk.
[Trait("Category", "unit")]
public class HudSettingsOverridesTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void NothingMovedWritesNothing()
    {
        Assert.Null(ConfigOverrides.Diff(new ConicArcStrokeConfig(), new ConicArcStrokeConfig(), Options));
        Assert.Null(ConfigOverrides.Diff(new UtteranceCountdownConfig(), new UtteranceCountdownConfig(), Options));
    }

    // Louis, 2026-10-14: "si je bouge quelque chose, il faut que ça
    // sauvegarde juste cette chose-là qui a bougé et pas le reste".
    [Fact]
    [Trait("Category", "regression")]
    public void OnlyTheMovedFieldIsWritten()
    {
        var edited = new ConicArcStrokeConfig { StrokeThickness = 6f };

        var overrides = ConfigOverrides.Diff(new ConicArcStrokeConfig(), edited, Options);

        Assert.NotNull(overrides);
        var key = Assert.Single(overrides.Select(pair => pair.Key));
        Assert.Equal("strokeThickness", key);
        Assert.Equal(6f, overrides["strokeThickness"]!.GetValue<float>());
    }

    [Fact]
    public void AFieldMovedBackToItsDefaultLeavesTheFile()
    {
        var stored = new JsonObject { ["strokeThickness"] = 6f };
        var effective = ConfigOverrides.Apply(new ConicArcStrokeConfig(), stored, Options);
        Assert.Equal(6f, effective.StrokeThickness);

        var backToDefault = new ConicArcStrokeConfig { StrokeThickness = new ConicArcStrokeConfig().StrokeThickness };
        Assert.Null(ConfigOverrides.Diff(new ConicArcStrokeConfig(), backToDefault, Options));
    }

    [Fact]
    public void ApplyRestoresEveryWrittenStrokeField()
    {
        var edited = EveryFieldMoved();

        var overrides = ConfigOverrides.Diff(new ConicArcStrokeConfig(), edited, Options);
        var restored = ConfigOverrides.Apply(new ConicArcStrokeConfig(), overrides, Options);

        Assert.NotNull(overrides);
        Assert.Equal(
            typeof(ConicArcStrokeConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance).Length,
            overrides.Count);
        Assert.Equal(edited, restored);
    }

    [Fact]
    public void ApplyRestoresTheCountdown()
    {
        var edited = new UtteranceCountdownConfig { StartReach = -0.3f };

        var overrides = ConfigOverrides.Diff(new UtteranceCountdownConfig(), edited, Options);
        var restored = ConfigOverrides.Apply(new UtteranceCountdownConfig(), overrides, Options);

        Assert.NotNull(overrides);
        Assert.Single(overrides.Select(pair => pair.Key));
        Assert.Equal(edited, restored);
    }

    [Fact]
    public void ApplyRestoresEveryWrittenCountdownField()
    {
        var edited = (UtteranceCountdownConfig)EveryFieldMoved(new UtteranceCountdownConfig());

        var overrides = ConfigOverrides.Diff(new UtteranceCountdownConfig(), edited, Options);
        var restored = ConfigOverrides.Apply(new UtteranceCountdownConfig(), overrides, Options);

        Assert.NotNull(overrides);
        Assert.Equal(
            typeof(UtteranceCountdownConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance).Length,
            overrides.Count);
        Assert.Equal(edited, restored);
    }

    // A property renamed or removed in code leaves a stale key behind in the
    // user's file; it must not break the load.
    [Fact]
    public void AKeyTheConfigNoLongerHasIsIgnored()
    {
        var stored = new JsonObject { ["gone"] = 1f, ["insetDip"] = -5f };

        var effective = ConfigOverrides.Apply(new ConicArcStrokeConfig(), stored, Options);

        Assert.Equal(-5f, effective.InsetDip);
        Assert.Equal(new ConicArcStrokeConfig { InsetDip = -5f }, effective);
    }

    // A hand-edited value, or a field whose type changed in code, must cost
    // that one field its override, not the HUD its whole configuration (the
    // callers have no try/catch around Apply).
    [Fact]
    [Trait("Category", "regression")]
    public void AValueOfTheWrongTypeIsIgnoredAndTheOtherOverridesStillApply()
    {
        var stored = new JsonObject { ["strokeThickness"] = "abc", ["insetDip"] = -5f };

        var effective = ConfigOverrides.Apply(new ConicArcStrokeConfig(), stored, Options);

        Assert.Equal(new ConicArcStrokeConfig { InsetDip = -5f }, effective);
    }

    // The gauge's cut once had its own move (cutSeconds, cutEase*) beside the
    // retreat's. The Retreat* keys became the one return, so a file written
    // before still applies them, ignores the Cut* keys it carries, and loses
    // them at the next save: Diff reads only the fields the config has.
    [Fact]
    public void ACountdownFileFromBeforeTheSharedReturnKeepsItsRetreatAndLosesItsCut()
    {
        double retreat = new UtteranceCountdownConfig().RetreatSeconds + 0.1;
        var stored = new JsonObject
        {
            ["retreatSeconds"] = retreat,
            ["cutSeconds"] = 0.2,
            ["cutEaseP1X"] = 0.7,
        };

        var effective = ConfigOverrides.Apply(new UtteranceCountdownConfig(), stored, Options);

        Assert.Equal(new UtteranceCountdownConfig { RetreatSeconds = retreat }, effective);

        var rewritten = ConfigOverrides.Diff(new UtteranceCountdownConfig(), effective, Options);

        Assert.NotNull(rewritten);
        Assert.Equal("retreatSeconds", Assert.Single(rewritten).Key);
    }

    // Every property nudged off its default, through the init accessors the
    // serializer itself uses.
    private static ConicArcStrokeConfig EveryFieldMoved()
        => (ConicArcStrokeConfig)EveryFieldMoved(new ConicArcStrokeConfig());

    private static object EveryFieldMoved(object boxed)
    {
        foreach (var property in boxed.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object current = property.GetValue(boxed)!;
            property.SetValue(boxed, current switch
            {
                float f  => f + 0.5f,
                double d => d + 0.25,
                int i    => i + 1,
                bool b   => !b,
                HudMaterial m => m == HudMaterial.Grey ? HudMaterial.Colour : HudMaterial.Grey,
                _ => throw new Xunit.Sdk.XunitException(
                    $"{property.Name} has an unhandled type {property.PropertyType.Name}; add a case for it"),
            });
        }
        return boxed;
    }
}
