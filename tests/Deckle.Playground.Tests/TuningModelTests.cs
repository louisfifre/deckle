using System.Reflection;
using Deckle.Hud;
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

    // The live HUD builds its stroke from HudStrokeTuning.ApplyAll; every
    // Playground knob must land there once published, and a reopened
    // Playground must read it back.
    [Fact]
    public void EveryFieldReachesTheLiveHudAndLoadsBack()
    {
        var fields = typeof(TuningModel).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var model = new TuningModel();
        for (int index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            field.SetValue(model, field.FieldType switch
            {
                var t when t == typeof(float) => 100 + index + 0.5f,
                var t when t == typeof(double) => 200 + index + 0.25,
                var t when t == typeof(int) => 300 + index,
                var t when t == typeof(bool) => !(bool)field.GetValue(model)!,
                var t => throw new Xunit.Sdk.XunitException(
                    $"{field.Name} has an unhandled type {t.Name}; add a sentinel for it"),
            });
        }

        var shipping = new TuningModel();
        try
        {
            model.PublishLive();
            var live = HudStrokeTuning.ApplyAll(new ConicArcStrokeConfig());
            var reloaded = new TuningModel();
            reloaded.LoadLive();

            foreach (var field in fields)
            {
                var property = typeof(ConicArcStrokeConfig)
                    .GetProperty(field.Name, BindingFlags.Public | BindingFlags.Instance)!;
                object? want = field.GetValue(model);
                object? got = property.GetValue(live);
                Assert.True(Equals(want, got), $"{field.Name}: published {want}, live HUD has {got}");
                object? back = field.GetValue(reloaded);
                Assert.True(Equals(want, back), $"{field.Name}: published {want}, reloaded {back}");
            }
        }
        finally { shipping.PublishLive(); }
    }
}
