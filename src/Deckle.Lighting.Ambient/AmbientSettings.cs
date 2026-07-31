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
    // AmbientEngine never starts — no screen capture, no Hue traffic,
    // zero CPU cost. Wiring of the runtime toggle happens in J3
    // (minimal end-to-end pipeline) ; until then the field is persisted
    // but has no consumer.
    public bool Enabled { get; set; } = false;

    // ── Hue bridge persistence (J3 step 2) ─────────────────────────
    //
    // Populated by the Playground after a successful Pair + group
    // selection. On subsequent app starts the Playground (and later
    // the AmbientPage) restores the HueBridgeClient from these values
    // without prompting the user to press the bridge's link button
    // again — the bridge keeps the username valid until manually
    // revoked from the Hue mobile app.
    //
    // All four properties default to null ; null means "not paired".
    // Cleared together when the user explicitly re-pairs (the new pair
    // overwrites them).

    /// <summary>LAN address of the paired bridge (e.g. 192.168.1.11).</summary>
    public string? HueBridgeIp { get; set; }

    /// <summary>Bridge serial / identifier (e.g. 001788FFFE3A2C18).</summary>
    public string? HueBridgeId { get; set; }

    /// <summary>Application username (CLIP API key) issued by the bridge
    /// at pairing. Sent on every REST call as the auth segment of the
    /// URL. Sensitive but recoverable — the user can revoke from the Hue
    /// mobile app.</summary>
    public string? HueUsername { get; set; }

    /// <summary>CLIP v1 id of the last group the user selected (e.g.
    /// "1", "5"). Used to pre-select the group in the Playground combo
    /// after restoring from settings.</summary>
    public string? HueLastGroupId { get; set; }

    // ── Monitor selection ──────────────────────────────────────────
    //
    // Win32 device name of the monitor the user picked as the capture
    // source (e.g. "\\\\.\\DISPLAY1"). Null = follow the primary, which
    // means follow whichever monitor Windows currently marks primary. A
    // non-null device name pins capture to that physical output; the capture
    // service falls back to primary if it is temporarily disconnected.

    /// <summary>Selected capture source. Null = follow the primary monitor.
    /// Non-null = a Win32 device name like "\\\\.\\DISPLAY1" obtained from
    /// <c>ScreenCaptureService.GetAvailableMonitors</c>.</summary>
    public string? SelectedMonitorDeviceName { get; set; }

    // ── Mode selection (J6 scaffolding) ────────────────────────────
    //
    // The enum and the property are pre-wired so a new behaviour lands
    // as a patch with no settings migration. Ambient is the default —
    // the smooth, low-saturation restitution of the scene's light, the
    // one that reads as room lighting rather than as an effect.

    /// <summary>Active mode preset. Defaults to <see cref="AmbientMode.Ambient"/>.
    /// Switching the mode (via the Playground or the Settings page)
    /// invokes <see cref="AmbientSettingsService.ApplyPreset(AmbientMode)"/>
    /// which copies the preset's tuning snapshot onto the other knobs.
    /// Touching any tuning slider in the Playground silently switches
    /// the mode to <see cref="AmbientMode.Custom"/>.</summary>
    public AmbientMode Mode { get; set; } = AmbientMode.Ambient;

    // ── Multi-light zones (J4) ─────────────────────────────────────
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
    // <see cref="LightZones"/> holds — useful for A/B comparison
    // without losing the zone assignments.

    /// <summary>Master switch for the multi-light pipeline. False keeps
    /// the legacy "one average → group action" behaviour. True activates
    /// per-zone sampling driven by <see cref="LightZones"/>, but only
    /// if the connected driver exposes
    /// <see cref="Deckle.Lighting.IMultiLightOutput"/> ; otherwise the
    /// engine logs a warning and falls back to single-colour push.</summary>
    public bool UseMultiLight { get; set; } = false;

    /// <summary>Per-light zone assignment, keyed by the driver's opaque
    /// light id (Hue : CLIP v1 integer-as-string). Empty by default ;
    /// the assignment UI populates it as the user picks a zone in the
    /// combo box of each light. Light ids missing from the map default
    /// to <see cref="LightZone.None"/> at sampling time, so a newly
    /// added bulb is skipped silently rather than tinted to an
    /// arbitrary edge.</summary>
    public Dictionary<string, LightZone> LightZones { get; set; } = new();

    /// <summary>Per-light brightness multiplier in [0, 1], keyed by the
    /// driver's opaque light id. 1.0 = full intensity (the sampled
    /// colour pushed verbatim), 0.5 = half (R/G/B halved before push,
    /// which also halves Hue's derived <c>bri</c>), 0 = effectively off
    /// (the off-threshold clamp kicks in and Hue receives <c>on:false</c>).
    /// Light ids missing from the map default to 1.0 — a newly added
    /// bulb runs at full brightness until the user adjusts its slider.
    /// Stored separately from <see cref="LightZones"/> so the user can
    /// dim a single lamp without losing its zone assignment.</summary>
    public Dictionary<string, double> LightBrightness { get; set; } = new();

    /// <summary>Which scale <see cref="BorderDepth"/> and
    /// <see cref="BorderCells"/> are interpreted on. By-share keeps the
    /// same fraction on every edge (the top band ends up thinner than
    /// the lateral bands on a 16:9 screen because there are fewer rows
    /// than columns in the sampler grid). By-cells keeps the same
    /// sampler-cell count on every edge — the band is the same number
    /// of preview cells thick on top as on the sides, regardless of
    /// aspect ratio. The Playground exposes this as a two-option
    /// picker ; the engine swaps the formula on every tick.</summary>
    public BorderThicknessMode BorderMode { get; set; } = BorderThicknessMode.Share;

    /// <summary>Border-band depth in <see cref="BorderThicknessMode.Share"/>
    /// mode, as a fraction of the matching grid dimension. The top zone
    /// reads cells in y ∈ [0, <see cref="BorderDepth"/>], the bottom in
    /// y ∈ [1 − <see cref="BorderDepth"/>, 1], the left in
    /// x ∈ [0, <see cref="BorderDepth"/>], the right in
    /// x ∈ [1 − <see cref="BorderDepth"/>, 1]. Range of practical
    /// interest [0.05, 0.5] ; the slider in the Playground clamps to
    /// that interval. Default 0.33 — the V0 lateral value, a
    /// one-third slice that works for a 3-lamp Top + Left + Right
    /// setup without over-summarising the frame.</summary>
    public double BorderDepth { get; set; } = 0.33;

    /// <summary>Border-band depth in <see cref="BorderThicknessMode.Cells"/>
    /// mode, expressed directly in sampler-grid cells (a 60×33 grid on
    /// a 4K monitor at mip 6, so each cell covers ~64×64 source pixels).
    /// Same cell count applied on every edge — top, bottom, left, right
    /// — so the band feels equally thick on every side regardless of
    /// the screen aspect ratio. Range of practical interest [4, 24]
    /// stepping by 2 ; the Playground slider clamps and snaps to that
    /// grid so every position lands on an even cell count. 24 cells
    /// on a 60×33 grid covers ~73 % of the vertical axis and ~40 % of
    /// the horizontal — enough headroom for narrow-aspect monitors or
    /// 5:4 setups while still leaving room for the opposing band when
    /// both edges are mapped. Default 8 — roughly matches the V0
    /// vertical 40 % share on a 60×33 grid without making the lateral
    /// bands eat the whole frame.</summary>
    public int BorderCells { get; set; } = 8;

    // ── HDR tuning ─────────────────────────────────────────────────
    //
    // User-tunable colour-grading controls (exposure / saturation /
    // lift / response curve). The detailed sliders live in the
    // Playground; AmbientEngine reads the settings on every tick so
    // changes apply live without restarting the pipeline.
    //
    // Why these four :
    //   - Exposure compensates for scRGB content peaking well below
    //     the display's reported MaxLuminance on a typical scene,
    //     which leaves the post-tone-map output dim. +1 EV roughly
    //     doubles brightness, restoring "Hue Sync" presence.
    //   - Saturation boost compensates for the de-saturation that
    //     happens when spatially averaging bright + dark pixels (the
    //     average drifts toward grey). Applied in OKLCh so hue stays
    //     stable and perceived luminance doesn't drift across the
    //     hue wheel.
    //   - Min brightness compensates for HueColorMath deriving bri
    //     from max(R,G,B) — a mid-tone scene like (60, 40, 80) gives
    //     bri ≈ 31 %, dim enough that the lamp's diffuser swallows
    //     the colour. A floor of ~180 keeps the chromaticity
    //     readable on the lamp without manual scene-by-scene
    //     adjustment. The floor has its own switch so the user can
    //     choose between true black and a readable low-light floor
    //     without losing the chosen floor value.
    //   - Brightness curve reshapes the max-channel response through
    //     one cubic-Bézier easing. It replaces the previous family
    //     picker (Linear / Gamma / S-Curve / Logarithmic) with one
    //     directly manipulable shape. Applied as a uniform RGB scale
    //     so xy chromaticity stays invariant — only bri moves.

    /// <summary>Exposure compensation in EV (stops of light) applied
    /// in linear-light before the tone-map. 0 = no change (default),
    /// +1 doubles brightness, -1 halves it. Range of practical
    /// interest [-2, +2]. Tuned in AmbientPage.</summary>
    public double ExposureEv { get; set; } = 0.0;

    /// <summary>Chroma multiplier applied to each sampled colour
    /// before push. 1.0 = no change (default), 2.0 = double
    /// saturation, 0.0 = greyscale. Range of practical interest
    /// [0, 2]. Applied in HSV-S to keep hue stable.</summary>
    public double SaturationBoost { get; set; } = 1.0;

    /// <summary>Whether <see cref="MinBrightness"/> is applied to the
    /// derived Hue <c>bri</c>. False keeps black / near-black scenes
    /// free to go fully dark ; true lifts any non-dark scene to the
    /// stored floor.</summary>
    public bool MinBrightnessEnabled { get; set; } = true;

    /// <summary>Floor for the bri value pushed to Hue, in the bridge's
    /// 0–254 range. The derived bri (max-channel based) is raised to
    /// this floor only when <see cref="MinBrightnessEnabled"/> is true
    /// and the lamp is on (i.e. above OffThreshold), so mid-tone scenes
    /// don't dim the lamp below readability. 254 forces full brightness
    /// for any non-dark scene. Default 180 ≈ 70 % — bright enough to
    /// colour the room, dim enough to follow the screen's intent.
    /// Tuned in AmbientPage.</summary>
    public int MinBrightness { get; set; } = 180;

    /// <summary>First cubic-Bézier control point X coordinate in [0, 1].
    /// The anchors are fixed at (0,0) and (1,1), so the four stored
    /// values are directly equivalent to CSS <c>cubic-bezier()</c>.
    /// The shipping default keeps the previous gamma-like feel :
    /// shadows are held low while highlights still reach full output.</summary>
    public double BrightnessCurveX1 { get; set; } = 0.42;

    /// <summary>First cubic-Bézier control point Y coordinate in [0, 1].
    /// Lower values squash dim scenes ; higher values lift them.</summary>
    public double BrightnessCurveY1 { get; set; } = 0.00;

    /// <summary>Second cubic-Bézier control point X coordinate in [0, 1].</summary>
    public double BrightnessCurveX2 { get; set; } = 1.00;

    /// <summary>Second cubic-Bézier control point Y coordinate in [0, 1].</summary>
    public double BrightnessCurveY2 { get; set; } = 1.00;

    /// <summary>Sum-of-absolute-channel-deltas threshold that gates
    /// pushes — if the new tuned-and-smoothed colour differs from the
    /// last pushed one by less than this on the 0-765 scale, the
    /// push is dropped. 0 disables the gate, useful when smoothing
    /// already does the heavy lifting. Default 6 (raised from the
    /// legacy 3) damps the residual quantisation noise without
    /// killing perceptible motion. Tuned in the Playground.</summary>
    public int ChangeThreshold { get; set; } = 6;

    /// <summary>Exponential moving average factor applied to the colour
    /// pushed to each light, in [0, 1]. 1.0 disables the filter
    /// (instantaneous response, legacy behaviour). Lower values trade
    /// reactivity for stability — 0.30 (the default) damps over
    /// roughly 3-5 frames at 15 Hz, which is enough to swallow the
    /// jitter of small moving reflections in a globally dark scene
    /// without dulling real scene changes. Below ~0.10 the lamp lags
    /// perceptibly during fast cuts. Applied per-light in multi
    /// mode, on the single group colour in group mode. Range of
    /// practical interest [0.05, 1.0]. Tuned in the Playground.</summary>
    public double SmoothingAlpha { get; set; } = 0.30;
}

/// <summary>How <see cref="AmbientEngine"/> derives the colour pushed
/// to the lights. The active value lives in <see cref="AmbientSettings.Mode"/>.
/// Game / Movie / Ambient carry preset tunings ; Custom is the
/// implicit mode the user lands on as soon as they move a slider in
/// the Playground, signalling "I'm tuning my own thing — don't
/// overwrite my values when a preset changes".</summary>
public enum AmbientMode
{
    /// <summary>Game / Ambilight — direct mapping with vivid saturation
    /// and quick response. Default for desktop play sessions.</summary>
    Game,

    /// <summary>Movie — softened saturation, longer smoothing, slight
    /// negative EV. Reads as ambient mood lighting for cinematic
    /// content where fast colour changes would distract.</summary>
    Movie,

    /// <summary>Ambient — heavy smoothing, low saturation, dark-friendly
    /// curve. Tuned for general-purpose room lighting that follows
    /// the screen without ever feeling like it competes with the
    /// content.</summary>
    Ambient,

    /// <summary>Custom — every tuning knob lives where the user put it.
    /// The Playground sets this implicitly the moment any slider is
    /// touched, so a preset switch never silently overwrites a hand-
    /// calibrated setup.</summary>
    Custom,
}

/// <summary>How the zone-sampling band thickness is expressed in
/// <see cref="AmbientSettings"/>. Lets the user pick the scale that
/// matches their mental model of "thickness" — equal share of the
/// screen on each side, or equal sampler-cell count.</summary>
public enum BorderThicknessMode
{
    /// <summary>Thickness as a fraction of the matching grid dimension
    /// (top / bottom use the row count, left / right use the column
    /// count). At 25 % on a 16:9 grid the top band ends up thinner in
    /// cells than the lateral bands — the proportion stays right, the
    /// per-edge cell count doesn't.</summary>
    Share,

    /// <summary>Thickness as a fixed cell count, applied identically on
    /// every edge. The top band reads at the same number of sampler
    /// cells as the lateral bands regardless of screen aspect ratio —
    /// the visual feel is symmetric on widescreen monitors where
    /// percentages make the top and bottom bands feel too thin
    /// compared to the sides.</summary>
    Cells,
}
