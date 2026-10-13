namespace Deckle.Lighting.Ambient;

// Container POCO grouping every Ambient-Light-scoped section under a
// single node persisted at <UserDataRoot>/modules/ambient/settings.json.
// Each module owns its own settings POCO; the consumer code reads from
// AmbientSettingsService.Instance.Current.
//
// Hue bridge coordinates stored here are the Ambient user's selected
// target: bridge IP / id, the application username, and the last
// selected light group. The Entertainment v2 DTLS pre-shared key
// (clientkey) is intentionally not stored in this JSON file; the Hue
// driver persists it in Deckle.Security's DPAPI-backed vault.
public sealed class AmbientSettings
{
    // Master toggle for the Ambient Light module. When false, the
    // AmbientEngine never starts — no screen capture, no Hue traffic.
    public bool Enabled { get; set; } = false;

    // ── Hue bridge persistence ─────────────────────────────────────
    //
    // HuePairingService writes the bridge fields at pairing and restores
    // the HueBridgeClient from them on first access, with no new
    // link-button press: the bridge keeps the username valid until it is
    // revoked from the Hue mobile app. Null means "not paired"; Forget
    // clears all four.

    public string? HueBridgeIp { get; set; }

    public string? HueBridgeId { get; set; }

    /// <summary>Application username (CLIP API key) issued by the bridge
    /// at pairing. Sensitive but revocable from the Hue mobile app.</summary>
    public string? HueUsername { get; set; }

    /// <summary>CLIP v1 id of the selected group (e.g. "1", "5"), not a
    /// v2 UUID.</summary>
    public string? HueLastGroupId { get; set; }

    /// <summary>Selected capture source. Null = follow whichever monitor
    /// Windows currently marks primary. Non-null = a Win32 device name like
    /// "\\\\.\\DISPLAY1" from <c>ScreenCaptureService.GetAvailableMonitors</c>;
    /// the capture service falls back to primary while that output is
    /// disconnected.</summary>
    public string? SelectedMonitorDeviceName { get; set; }

    /// <summary>Screen-capture rate in Hz, a closed choice among
    /// <see cref="AmbientCaptureRate.Allowed"/>; the engine snaps any other
    /// stored value with <see cref="AmbientCaptureRate.Snap"/>. 30 or 15
    /// lighten the GPU readback on weaker machines. The push to the lights
    /// keeps its own transport rate. Read at engine start; the app restarts
    /// a running engine when it changes (<see cref="AmbientStartSettings"/>).</summary>
    public int CaptureFrameRateHz { get; set; } = AmbientCaptureRate.Default;

    // ── Preset selection (Deckle.Presets) ──────────────────────────
    //
    // The presets fix the eight tuning values below (AmbientTuning); the
    // family on AmbientSettingsService.Presets selects, applies and keeps
    // the Custom values. Ambient is the default — the smooth,
    // low-saturation restitution of the scene's light, the one that
    // reads as room lighting rather than as an effect.
    //
    // An older file may hold an integer "mode" instead; it is ignored and
    // the selection is derived once from the values (the preset they
    // equal, else Custom with those values kept).

    /// <summary>The stored selection: a preset id from
    /// <see cref="AmbientPresets"/>, or <c>"custom"</c>. Null until the
    /// family derives it from the values, on the first load of an older
    /// file.</summary>
    public string? Preset { get; set; }

    /// <summary>The user's own tuning, kept while a preset is selected so
    /// picking Custom brings it back. Null until the first hand edit.</summary>
    public AmbientTuning? CustomTuning { get; set; }

    // ── Multi-light zones ──────────────────────────────────────────
    //
    // When the connected output is an IMultiLightOutput and the engine
    // is told to run in multi-light mode, it pushes one colour per
    // zone — top / bottom / left / right border of the screen — and
    // every light assigned to a zone via <see cref="LightZones"/>
    // receives that colour. Lights mapped to <see cref="LightZone.None"/>
    // (or absent from the map entirely) are not driven by the engine
    // and keep whatever state the bridge last gave them.
    //
    // When <see cref="UseMultiLight"/> is false (default), the engine
    // falls back to the single-colour group push regardless of what
    // <see cref="LightZones"/> holds, so the zone assignments survive
    // switching back and forth.

    /// <summary>Master switch for the multi-light pipeline, read at engine
    /// start. True only takes effect if the connected driver exposes
    /// <see cref="Deckle.Lighting.IMultiLightOutput"/> ; otherwise the
    /// engine logs a warning and falls back to single-colour push.</summary>
    public bool UseMultiLight { get; set; } = false;

    /// <summary>Per-light zone assignment, keyed by the driver's opaque
    /// light id (Hue : CLIP v1 integer-as-string). Light ids missing from
    /// the map default to <see cref="LightZone.None"/> at sampling time,
    /// so a newly added bulb is skipped silently rather than tinted to an
    /// arbitrary edge.</summary>
    public Dictionary<string, LightZone> LightZones { get; set; } = new();

    /// <summary>Per-light brightness multiplier in [0, 1], keyed by the
    /// driver's opaque light id. Scaling R/G/B also scales Hue's derived
    /// <c>bri</c>. 0 = off: the floor is skipped and Hue receives
    /// <c>on:false</c>. Light ids missing from the map default to 1.0.
    /// Stored separately from <see cref="LightZones"/> so the user can
    /// dim a single lamp without losing its zone assignment.</summary>
    public Dictionary<string, double> LightBrightness { get; set; } = new();

    /// <summary>Which scale <see cref="BorderDepth"/> and
    /// <see cref="BorderCells"/> are interpreted on. By-share keeps the
    /// same fraction on every edge (the top band ends up thinner than
    /// the lateral bands on a 16:9 screen because there are fewer rows
    /// than columns in the sampler grid). By-cells keeps the same
    /// sampler-cell count on every edge.</summary>
    public BorderThicknessMode BorderMode { get; set; } = BorderThicknessMode.Share;

    /// <summary>Border-band depth in <see cref="BorderThicknessMode.Share"/>
    /// mode, as a fraction of the matching grid dimension: the top zone
    /// reads y ∈ [0, depth], the bottom y ∈ [1 − depth, 1], and likewise
    /// on x for left and right.</summary>
    public double BorderDepth { get; set; } = 0.33;

    /// <summary>Border-band depth in <see cref="BorderThicknessMode.Cells"/>
    /// mode, in sampler-grid cells (the grid is 60×33 on a 4K or 1080p
    /// screen), the same count on every edge.</summary>
    public int BorderCells { get; set; } = 8;

    // ── Tuning ─────────────────────────────────────────────────────
    //
    // User-tunable colour-grading controls (exposure / saturation /
    // floor / response curve) and motion controls (reaction speed /
    // change threshold). AmbientEngine reads them on every tick, so
    // changes apply live without restarting the pipeline. They apply to
    // SDR and HDR alike: HDR is tone-mapped to 8 bits before this stage.
    //
    // The initializers of the eight preset-covered values (everything
    // but the floor) ARE the Ambient preset, which AmbientPresets reads
    // from a fresh instance: one source for a fresh install, the per-card
    // reset and the page reset.
    //
    // Why these four :
    //   - Exposure compensates for scRGB content peaking well below
    //     the display's reported MaxLuminance on a typical scene,
    //     which leaves the post-tone-map output dim.
    //   - Saturation boost compensates for the de-saturation that
    //     happens when spatially averaging bright + dark pixels (the
    //     average drifts toward grey). Applied in OKLCh so hue stays
    //     stable and perceived luminance doesn't drift across the
    //     hue wheel.
    //   - Min brightness compensates for HueColorMath deriving bri
    //     from max(R,G,B): a mid-tone scene comes out dim enough that
    //     the lamp's diffuser swallows the colour. The floor has its
    //     own switch so the user can choose between true black and a
    //     readable low-light floor without losing the floor value.
    //   - Brightness curve reshapes the max-channel response through
    //     one cubic-Bézier easing, applied as a uniform RGB scale so
    //     xy chromaticity stays invariant — only bri moves.

    /// <summary>Exposure compensation in EV (stops of light) applied
    /// in linear-light before the tone-map. 0 = no change, +1 doubles
    /// brightness, -1 halves it. The Ambient preset's -0.5 keeps the
    /// room a little under the screen.</summary>
    public double ExposureEv { get; set; } = -0.5;

    /// <summary>Chroma multiplier applied to each sampled colour
    /// before push. 1.0 = no change, 2.0 = double saturation, 0.0 =
    /// greyscale. The Ambient preset's 0.7 stays muted so the lights
    /// never compete with the content.</summary>
    public double SaturationBoost { get; set; } = 0.7;

    /// <summary>Whether <see cref="MinBrightness"/> is applied to the
    /// derived Hue <c>bri</c>. False lets black / near-black scenes go
    /// fully dark; true lifts every scene, including black, to the
    /// stored floor.</summary>
    public bool MinBrightnessEnabled { get; set; } = true;

    /// <summary>Floor for the bri value pushed to Hue, in the bridge's
    /// 0–254 range. The derived bri (max-channel based) is raised to
    /// this floor whenever <see cref="MinBrightnessEnabled"/> is true,
    /// so neither mid-tone nor black scenes can dim the lamp below it.
    /// Pure black has no chromaticity, so it becomes neutral RGB at the
    /// floor. 254 forces full brightness. The default keeps a readable
    /// tint on the lamp in a dim scene while the room stays close to
    /// dark.</summary>
    public int MinBrightness { get; set; } = 40;

    /// <summary>First cubic-Bézier control point X coordinate in [0, 1].
    /// The anchors are fixed at (0,0) and (1,1), so the four stored
    /// values are directly equivalent to CSS <c>cubic-bezier()</c>.
    /// The default curve is the Ambient preset's: it lifts dark scenes
    /// so a dim room still reads as lit.</summary>
    public double BrightnessCurveX1 { get; set; } = 0.18;

    /// <summary>First cubic-Bézier control point Y coordinate in [0, 1].
    /// Lower values squash dim scenes ; higher values lift them.</summary>
    public double BrightnessCurveY1 { get; set; } = 0.55;

    public double BrightnessCurveX2 { get; set; } = 0.40;

    public double BrightnessCurveY2 { get; set; } = 0.90;

    /// <summary>Sum-of-absolute-channel-deltas threshold that gates
    /// pushes — if the new tuned-and-smoothed colour differs from the
    /// last pushed one by less than this on the 0-765 scale, the
    /// previous colour stays : a persistent output (Hue REST) gets no
    /// push, a continuous one (Hue Entertainment) gets the previous
    /// colour again. A change to exact black always goes out
    /// (AmbientPushGate). 0 disables the gate. On a slow fade a
    /// threshold shows as small steps.</summary>
    public int ChangeThreshold { get; set; } = 10;

    /// <summary>Exponential moving average factor applied to the colour
    /// pushed to each light, in [0, 1]. 1.0 disables the filter.
    /// Lower values trade reactivity for stability. The value is defined
    /// per push tick at the 15 Hz reference rate and adapted to the
    /// measured time between push ticks (AmbientPushCadence.AdaptSmoothingAlpha);
    /// it does not depend on the capture rate. Applied per-light in multi
    /// mode, on the single group colour in group mode.</summary>
    public double SmoothingAlpha { get; set; } = 0.08;
}

/// <summary>How the zone-sampling band thickness is expressed in
/// <see cref="AmbientSettings"/>. Lets the user pick the scale that
/// matches their mental model of "thickness" — equal share of the
/// screen on each side, or equal sampler-cell count.</summary>
public enum BorderThicknessMode
{
    /// <summary>Thickness as a fraction of the matching grid dimension
    /// (top / bottom use the row count, left / right use the column
    /// count).</summary>
    Share,

    /// <summary>Thickness as a fixed cell count, applied identically on
    /// every edge, so widescreen top and bottom bands do not feel thin
    /// next to the sides.</summary>
    Cells,
}
