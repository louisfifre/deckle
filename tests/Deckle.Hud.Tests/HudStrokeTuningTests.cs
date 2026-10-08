using System.Reflection;
using Deckle.Composition;
using Xunit;

namespace Deckle.Hud.Tests;

// The three live tuning sources are process-wide statics; the tests that
// write them share one collection so xunit never runs them in parallel.
[CollectionDefinition(Name)]
public sealed class HudLiveTuningCollection
{
    public const string Name = "HUD live tuning";
}

[Trait("Category", "unit")]
[Collection(HudLiveTuningCollection.Name)]
public sealed class HudStrokeTuningTests
{
    [Fact]
    public void EveryStrokeFieldReachesTheLiveStrokeConfig()
    {
        var tuned = WithEveryFieldChanged(new HudComposition.ConicArcStrokeConfig());
        var (material, geometry, stroke) = (HudMaterialTuning.Current, HudGeometryTuning.Current, HudStrokeTuning.Current);
        try
        {
            HudMaterialTuning.Apply(tuned);
            HudGeometryTuning.Apply(tuned);
            HudStrokeTuning.Apply(tuned);

            var live = HudStrokeTuning.ApplyAll(new());

            foreach (var property in Properties)
            {
                object? want = property.GetValue(tuned);
                object? got = property.GetValue(live);
                Assert.True(Equals(want, got), $"{property.Name}: tuned {want}, live {got}");
            }
        }
        finally
        {
            HudMaterialTuning.Apply(material);
            HudGeometryTuning.Apply(geometry);
            HudStrokeTuning.Apply(stroke);
        }
    }

    [Fact]
    public void StrokeSourceLeavesMaterialAndGeometryToTheirSources()
    {
        var original = HudStrokeTuning.Current;
        try
        {
            HudStrokeTuning.Apply(original with
            {
                RecordingConicSpanTurns = 0.3f, OklchChroma = 0.2f, StrokeThickness = 9f,
            });
            var target = new HudComposition.ConicArcStrokeConfig { OklchChroma = 0.05f, StrokeThickness = 2f };
            var actual = HudStrokeTuning.ApplyTo(target);
            Assert.Equal(0.3f, actual.RecordingConicSpanTurns);
            Assert.Equal(target.OklchChroma, actual.OklchChroma);
            Assert.Equal(target.StrokeThickness, actual.StrokeThickness);
        }
        finally { HudStrokeTuning.Apply(original); }
    }

    private static readonly PropertyInfo[] Properties =
        typeof(HudComposition.ConicArcStrokeConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    // Distinct non-default value per property, so a field copied into the
    // wrong slot or dropped by every source shows up by name.
    private static HudComposition.ConicArcStrokeConfig WithEveryFieldChanged(HudComposition.ConicArcStrokeConfig config)
    {
        object boxed = config;
        for (int index = 0; index < Properties.Length; index++)
        {
            var property = Properties[index];
            object value = property.PropertyType switch
            {
                var t when t == typeof(float) => 100 + index + 0.5f,
                var t when t == typeof(double) => 200 + index + 0.25,
                var t when t == typeof(int) => 300 + index,
                var t when t == typeof(bool) => !(bool)property.GetValue(boxed)!,
                var t => throw new Xunit.Sdk.XunitException(
                    $"{property.Name} has an unhandled type {t.Name}; add a sentinel for it"),
            };
            property.SetValue(boxed, value);
        }
        return (HudComposition.ConicArcStrokeConfig)boxed;
    }
}
