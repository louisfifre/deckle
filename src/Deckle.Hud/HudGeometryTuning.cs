using Deckle.Composition;

namespace Deckle.Hud;

// Process-local geometry shared by the base stroke across HUD states.
// Countdown geometry and material settings remain independent.
public static class HudGeometryTuning
{
    private sealed record Snapshot(HudComposition.ConicArcStrokeConfig Config);
    private static Snapshot _current = new(new());
    public static HudComposition.ConicArcStrokeConfig Current => Volatile.Read(ref _current).Config;
    public static event Action? Changed;

    public static void Apply(HudComposition.ConicArcStrokeConfig config)
    {
        var geometry = CopyGeometry(config, new());
        if (geometry.Equals(Current)) return;
        Volatile.Write(ref _current, new Snapshot(geometry));
        Changed?.Invoke();
    }

    public static HudComposition.ConicArcStrokeConfig ApplyTo(HudComposition.ConicArcStrokeConfig target)
        => CopyGeometry(Current, target);

    private static HudComposition.ConicArcStrokeConfig CopyGeometry(
        HudComposition.ConicArcStrokeConfig source, HudComposition.ConicArcStrokeConfig target) => target with
    {
        StrokeThickness = source.StrokeThickness,
        InsetDip = source.InsetDip,
    };
}
