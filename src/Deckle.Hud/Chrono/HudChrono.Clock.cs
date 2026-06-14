using Deckle.Chrono;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Hud;

// HudChrono — the chronometer face and its functional lifecycle.
//
// Turns the ChronoTimer's elapsed value into the six MM.SS.cc glyphs, and
// owns the start / stop / reset of that timer. The lifecycle is deliberately
// separate from the Apply* paint methods (HudChrono.xaml.cs): ApplyState only
// styles the card, it never touches the clock value or the digit text — so the
// same look can be painted over either a zeroed clock (session start) or a
// frozen one (end of take). The host (HudWindow.SetState) pairs each transition
// with the matching clock op; the Playground drives the same public methods.
public sealed partial class HudChrono
{
    // Cross-assembly hook for the recording cap used by UpdateClock to
    // freeze the chrono at the configured ceiling. The shipping App wires
    // this to Audio.CaptureSettingsService.Instance.Current.MaxRecordingDurationSeconds
    // at boot; until wired, the default `int.MaxValue` is a no-op (no cap),
    // which keeps the lib usable standalone (HudPlayground tests, future
    // host modules) without a Settings module dependency.
    //
    // Read by ChronoFormatter.Decompose on every vsync tick — must be
    // cheap (a single delegate invoke). Mutating it live is allowed; the
    // change takes effect on the next render frame.
    public static System.Func<int> MaxRecordingDurationSecondsProvider { get; set; }
        = () => int.MaxValue;

    private readonly ChronoTimer _stopwatch = new();

    private int _lastMin = -1;
    private int _lastSec = -1;
    private int _lastCs  = -1;

    // Resolved at runtime from Application.Resources so theme switches
    // still update the brush (System resource keys flip color across
    // light/dark). Only ResolveNeutralBrush is used from code — the
    // critical / primary brushes now live purely in XAML via
    // {ThemeResource …} bindings on the accent overlay TextBlocks and
    // the shared ChronoDigitStyle, respectively, and theme changes are
    // handled by WinUI's resource-tracking machinery. The neutral brush
    // is the one exception because Charging overrides the primary
    // Foreground with the tertiary tone on every digit — too
    // state-specific to express as a ThemeResource default.
    private static Brush ResolveNeutralBrush() =>
        (Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush)
        ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);

    // ── Clock lifecycle ───────────────────────────────────────────────────────
    //
    // The functional chronometer, owned here and driven by the host
    // (HudWindow.SetState maps each transition to one of these) and the
    // Playground. Deliberately kept OUT of the Apply* paint methods: ApplyState
    // only styles the card — it never starts, stops, or zeroes the clock. That
    // separation is what lets the same visual (e.g. the parked Charging look) be
    // painted over either a zeroed clock at session start or a frozen one at the
    // end of a take, without the paint deciding the elapsed value.

    // Zero the clock and the digit face. Called on a new session (Charging
    // entry) so a take never opens on the previous take's frozen value — the
    // bug the old in-ApplyCharging reset papered over, now an explicit step.
    public void ResetClock()
    {
        _stopwatch.Reset();
        ClearDigitDisplay();
    }

    // Restart from zero and begin ticking. Robust to entry without a preceding
    // ResetClock (the warm path can show Recording directly): clears the face
    // first so the jump to 00.00.00 doesn't flash the digits that differ from a
    // leftover frozen value.
    public void StartClock()
    {
        ClearDigitDisplay();
        _stopwatch.Start();   // ChronoTimer.Start == Stopwatch.Restart (zero + run)
        UpdateClock();        // paint 00.00.00 before the first vsync tick
        HookRendering();      // vsync drives UpdateClock while running
    }

    // Freeze on the final value. UpdateClock latches the elapsed reached between
    // the last vsync tick and the Stop (may light the last-changed digit) — it
    // must run before the swipe starts, which the caller guarantees by calling
    // StopClock before SetState(Transcribing).
    public void StopClock()
    {
        _stopwatch.Stop();
        UpdateClock();
    }

    // Reset the visible chrono face to a pristine zero: invalidate the
    // last-rendered cache so UpdateClock repaints every position, drop the
    // per-digit "ever-changed" flags and accent heat (the red flash state), and
    // write the glyphs straight to "0" via ResetDigitTexts (not WriteDigit,
    // which would treat the change as a tick and flash it).
    private void ClearDigitDisplay()
    {
        _lastMin = _lastSec = _lastCs = -1;
        ClearDigitChanged();
        ClearDigitHeat();
        ResetDigitTexts();
    }

    private void ResetDigitTexts()
    {
        Min1.Text = Min2.Text = "0";
        Sec1.Text = Sec2.Text = "0";
        Cs1.Text  = Cs2.Text  = "0";
        DotA.Text = DotB.Text = ".";
        // Keep the accent overlays' Text in sync even when they're hidden
        // — otherwise the next heat-up would show a stale digit briefly
        // before UpdateClock rewrites it.
        Min1Accent.Text = Min2Accent.Text = "0";
        Sec1Accent.Text = Sec2Accent.Text = "0";
        Cs1Accent.Text  = Cs2Accent.Text  = "0";
    }

    // Clears the local Foreground on all 8 primary TextBlocks. Each falls
    // back to its Style default (TextFillColorPrimaryBrush). The accent
    // overlays always carry SystemFillColorCriticalBrush from XAML, so
    // they don't need clearing here — only the Opacity is state-driven.
    private void ClearDigitForegrounds()
    {
        Min1.ClearValue(TextBlock.ForegroundProperty);
        Min2.ClearValue(TextBlock.ForegroundProperty);
        DotA.ClearValue(TextBlock.ForegroundProperty);
        Sec1.ClearValue(TextBlock.ForegroundProperty);
        Sec2.ClearValue(TextBlock.ForegroundProperty);
        DotB.ClearValue(TextBlock.ForegroundProperty);
        Cs1.ClearValue(TextBlock.ForegroundProperty);
        Cs2.ClearValue(TextBlock.ForegroundProperty);
    }

    // Writes `newText` onto both the primary and accent TextBlocks at
    // `index`, flags the digit as "changed" for the downstream swipe,
    // and bumps its heat to 1 so the accent overlay is visible
    // immediately — the Recording-time "each change flashes red" UX
    // we have been iterating on. Returns early if the text didn't
    // actually change (no-op on every vsync for stationary digits).
    // Index order matches the swipe animator's per-element arrays:
    // 0 Min1, 1 Min2, 2 Sec1, 3 Sec2, 4 Cs1, 5 Cs2.
    private void WriteDigit(int index, string newText, TextBlock primary, TextBlock accent)
    {
        if (primary.Text == newText) return;
        primary.Text = newText;
        accent.Text  = newText;
        _swipe.SetChanged(index, true);
        _swipe.SetHeat(index, 1f);
        // Push the flash directly on the overlay. During Recording the
        // vsync loop (UpdateClock) runs but UpdateSwipe is dormant
        // (_swipeRunning=false), so without this line heat=1 never
        // reaches the overlay and the digit stays primary. During
        // Transcribing/Rewriting the swipe rewrites Opacity every
        // vsync — this write is immediately superseded by UpdateSwipe,
        // so no interference with the wave animation.
        //
        // Invariant primary.Opacity + accent.Opacity = 1 so only one
        // glyph ever contributes ink. Without this, both TextBlocks
        // render at full alpha simultaneously and the accent glyph
        // appears visibly thicker / bolder than an unchanged digit,
        // because two ClearType-hinted copies of the same glyph at
        // the same position double up on subpixel coverage.
        accent.Opacity  = 1;
        primary.Opacity = 0;
    }

    private void UpdateClock()
    {
        int capSec = MaxRecordingDurationSecondsProvider();
        var d = ChronoFormatter.Decompose(_stopwatch.Elapsed, capSec);
        int min = d.Minutes;
        int sec = d.Seconds;
        int cs  = d.Centiseconds;

        if (min != _lastMin)
        {
            int d1 = min / 10, d2 = min % 10;
            WriteDigit(0, d1.ToString(), Min1, Min1Accent);
            WriteDigit(1, d2.ToString(), Min2, Min2Accent);
            _lastMin = min;
        }
        if (sec != _lastSec)
        {
            int d1 = sec / 10, d2 = sec % 10;
            WriteDigit(2, d1.ToString(), Sec1, Sec1Accent);
            WriteDigit(3, d2.ToString(), Sec2, Sec2Accent);
            _lastSec = sec;
        }
        if (cs != _lastCs)
        {
            int d1 = cs / 10, d2 = cs % 10;
            WriteDigit(4, d1.ToString(), Cs1, Cs1Accent);
            WriteDigit(5, d2.ToString(), Cs2, Cs2Accent);
            _lastCs = cs;
        }
    }
}
