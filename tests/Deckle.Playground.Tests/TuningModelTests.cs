using System.Reflection;
using Xunit;
using static Deckle.Composition.HudComposition;

namespace Deckle.Playground.Tests;

// The Playground HUD tab tunes a mutable TuningModel and renders the
// preview from its ToConfig() projection. These tests pin the two facts
// that keep the preview honest: at defaults it renders exactly what the
// shipping HUD renders, and every knob actually reaches the config.
// Both walk the types by reflection, so a field added later is covered
// without editing the tests.
[Trait("Category", "unit")]
public class TuningModelTests
{
    [Fact]
    public void DefaultsMatchTheShippingConfig()
    {
        var expected = new ConicArcStrokeConfig();
        var actual = new TuningModel().ToConfig();

        foreach (var property in typeof(ConicArcStrokeConfig)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? want = property.GetValue(expected);
            object? got = property.GetValue(actual);
            Assert.True(Equals(want, got),
                $"{property.Name}: expected {want}, got {got}");
        }
    }

    [Fact]
    public void EveryFieldReachesTheConfig()
    {
        var fields = typeof(TuningModel).GetFields(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(fields);

        for (int index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            var property = typeof(ConicArcStrokeConfig)
                .GetProperty(field.Name, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(property is not null,
                $"{field.Name} has no same-named property on ConicArcStrokeConfig");

            var model = new TuningModel();
            object sentinel = field.FieldType switch
            {
                var t when t == typeof(float) => 100 + index + 0.5f,
                var t when t == typeof(double) => 200 + index + 0.25,
                var t when t == typeof(int) => 300 + index,
                var t when t == typeof(bool) => !(bool)field.GetValue(model)!,
                var t => throw new Xunit.Sdk.XunitException(
                    $"{field.Name} has an unhandled type {t.Name}; add a sentinel for it"),
            };
            field.SetValue(model, sentinel);

            object? projected = property!.GetValue(model.ToConfig());
            Assert.True(Equals(sentinel, projected),
                $"{field.Name}: set {sentinel}, ToConfig() returned {projected}");
        }
    }
}
