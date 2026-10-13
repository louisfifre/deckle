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
    // Recording cap that freezes the chrono at the configured ceiling. The App
    // wires it at boot; the default int.MaxValue means no cap, so this library
    // needs no Settings dependency. Read on every vsync tick, so it must stay
    // cheap; a live change applies on the next frame.
    public static System.Func<int> MaxRecordingDurationSecondsProvider { get; set; }
        = () => int.MaxValue;

    private readonly ChronoTimer _stopwatch = new();

    private int _lastMin = -1;
    private int _lastSec = -1;
    private int _lastCs  = -1;

    // ── Clock lifecycle ───────────────────────────────────────────────────────

    // Called on a new session (Charging entry) so a take never opens on the
    // previous take's frozen value.
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
    // the last vsync tick and the Stop (may light the last-changed digit), so it
    // must run before the processing reveal starts: HudWindow.SetState calls
    // StopClock before ApplyState(Transcribing).
    public void StopClock()
    {
        _stopwatch.Stop();
        UpdateClock();
    }

    // Writes the glyphs straight to "0" through ResetDigitTexts, not WriteDigit,
    // which would treat the change as a tick and flash it.
    private void ClearDigitDisplay()
    {
        _lastMin = _lastSec = _lastCs = -1;
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

    // Each digit change flashes in accent while recording.
    private void WriteDigit(int index, string newText, TextBlock primary, TextBlock accent)
    {
        if (primary.Text == newText) return;
        primary.Text = newText;
        accent.Text  = newText;
        // The flash is an instantaneous data state, not a temporal animation.
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
