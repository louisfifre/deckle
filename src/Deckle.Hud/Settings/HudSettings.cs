using System.Text.Json.Nodes;

namespace Deckle.Hud;

// On disk, `modules\hud\settings.json`. Each object holds only the fields of
// its config that differ from the compiled defaults (see ConfigOverrides), so
// a fresh install writes `{}` and a user who moved one slider has one key.
public sealed class HudSettings
{
    /// <summary>Overrides on HudComposition.ConicArcStrokeConfig.</summary>
    public JsonObject? Stroke { get; set; }

    /// <summary>Overrides on UtteranceCountdownConfig.</summary>
    public JsonObject? Countdown { get; set; }
}
