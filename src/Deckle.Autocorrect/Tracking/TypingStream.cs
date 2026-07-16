using System.Text;

namespace Deckle.Autocorrect;

// The typing-stream accumulator (CONTEXT.md § Typing stream): the verbatim flow
// of what is typed on enrolled correctable surfaces, recorded as runs. A run
// accumulates while typing flows forward and closes the moment a backward
// repair begins; the next run resumes from the repair point. Reading the runs
// in order restores everything — faulty forms as they stood on screen, what
// was erased, what was retyped, and clean sentences whole.
//
// Pure and synchronous, like SentenceCorpus: decoded keystrokes go in, closed
// runs come out through Completed; no OS calls, no EventSource — the engine
// owns emission, the surface tag and every consent gate. It is a parallel
// consumer of the same inputs the TypedWordTracker consumes (keystrokes,
// pointer, focus), never a reinterpretation of the tracker's word model.
//
// Reconstruction contract. Runs chain into a SPAN — a stretch of typing whose
// screen effect the stream fully modelled. Within a span, replay is: append
// the run's text; then, for the next run, delete `Erased` chars off the end
// and append its text. A run's closure says why it ended and whether the span
// survives it:
//   • "repair" — a Backspace began a backward repair; the span continues, the
//     erase count lands on the NEXT run's Erased.
//   • "cap"    — the run hit the size cap; a pure storage split, the span
//     continues seamlessly.
//   • everything else ends the span: "enter" (the line was committed),
//     "navigation", "escape", "shortcut", "delete", "deadkey" (the caret or
//     the field left the modelled stretch), "pointer", "focus" (same, by
//     mouse or by surface change). Text after a span end is a fresh span.
// Erased chars can exceed what the span recorded (backing into text typed
// before the span started): the first run of a span may then carry a non-zero
// Erased — honest, and the reader knows those chars are unknown territory. A
// repair that no forward typing follows (the field is left mid-erase) flushes
// as a text-less record carrying only its Erased and the closing reason.
//
// Timing rides per char: comma-joined gaps in ms since the previous keystroke
// seen in the span (first of a span "0"; the second UTF-16 unit of a surrogate
// pair "0" so the string stays aligned to Text). The first gap after a repair
// therefore includes the repair burst itself — the pause the pause-pass will
// one day want. Empty when no keystroke carried a clock, mirroring the corpus.
public sealed class TypingStream
{
    // A run is at most a long sentence; the cap only splits pathological
    // uninterrupted flows (closure "cap" chains them back together).
    internal const int RunCap = 512;

    // One closed run: the forward text, the backspaces that preceded it inside
    // the span, why it closed, and the per-char keystroke gaps.
    public readonly record struct RunRecord(string Text, int Erased, string Closure, string Timing);

    /// <summary>Raised when a run closes. The engine emits it on the
    /// dedicated, consent-gated dataset.</summary>
    public Action<RunRecord>? Completed;

    private readonly StringBuilder _text = new();
    private readonly List<int> _gaps = new();
    private bool _anyStamp;          // at least one keystroke of this run carried a clock
    private int _erasedBefore;       // backspaces between the previous run and this text
    private bool _erasing;           // inside a backward-repair burst
    private int _eraseCount;         // its size so far
    private double _lastKeyMs;       // previous keystroke's clock within the span (0 = none)

    public void OnKeystroke(Keystroke k)
    {
        switch (k.Kind)
        {
            case KeystrokeKind.Text:
                ProcessText(k);
                break;

            case KeystrokeKind.Backspace:
                ProcessBackspace(k.TimestampMs);
                break;

            case KeystrokeKind.Enter:
                CloseSpan("enter");
                break;

            case KeystrokeKind.Tab:
            case KeystrokeKind.Navigation:
                CloseSpan("navigation");
                break;

            case KeystrokeKind.Escape:
                CloseSpan("escape");
                break;

            case KeystrokeKind.Shortcut:
                CloseSpan("shortcut");
                break;

            case KeystrokeKind.Delete:
                CloseSpan("delete");
                break;

            case KeystrokeKind.DeadKey:
                CloseSpan("deadkey");
                break;

            case KeystrokeKind.Other:
                break; // irrelevant to the screen — no closure, no clock
        }
    }

    public void NotifyPointerInteraction() => CloseSpan("pointer");

    public void NotifyFocusChanged() => CloseSpan("focus");

    // Privacy boundary: drop everything in flight without invoking Completed.
    // Used when text collection consent is withdrawn or the engine stops.
    public void Discard()
    {
        _text.Clear();
        _gaps.Clear();
        _anyStamp = false;
        _erasedBefore = 0;
        _erasing = false;
        _eraseCount = 0;
        _lastKeyMs = 0;
    }

    private void ProcessText(Keystroke k)
    {
        if (_erasing)
        {
            // Forward typing resumes: the repair burst is over, its size lands
            // on the run now beginning (the previous run flushed at burst start).
            _erasedBefore = _eraseCount;
            _erasing = false;
            _eraseCount = 0;
        }

        int gap = GapSince(k.TimestampMs);
        foreach (char c in k.Text)
        {
            if (_text.Length >= RunCap)
                EmitRun("cap"); // a storage split — the span continues
            _text.Append(c);
            _gaps.Add(gap);
            gap = 0; // the second unit of a surrogate pair keeps alignment
        }
    }

    private void ProcessBackspace(double timestampMs)
    {
        if (_text.Length > 0)
        {
            // The moment a backward repair begins, the run closes. The burst's
            // size is only known when it ends — it rides on the next record.
            EmitRun("repair");
            _erasing = true;
            _eraseCount = 1;
        }
        else if (_erasing)
        {
            _eraseCount++;
        }
        else
        {
            // Nothing typed yet in this span: the erase bites into text from
            // before it (or before the stream was fed) — recorded all the same,
            // the reader knows a span's leading Erased eats unknown territory.
            _erasing = true;
            _eraseCount = 1;
        }
        GapSince(timestampMs); // keep the span clock honest through the burst
    }

    // The span is over — the caret, the field or the line left the modelled
    // stretch. A dangling repair folds into the flushed record's Erased (a
    // text-less record when nothing was retyped); an empty state emits nothing.
    private void CloseSpan(string closure)
    {
        if (_erasing)
        {
            _erasedBefore += _eraseCount;
            _erasing = false;
            _eraseCount = 0;
        }
        if (_text.Length > 0 || _erasedBefore > 0)
            EmitRun(closure);
        _erasedBefore = 0;
        _lastKeyMs = 0;
    }

    // Flushes the current run and starts the next one inside the same span.
    private void EmitRun(string closure)
    {
        string timing = _anyStamp ? string.Join(',', _gaps) : string.Empty;
        var record = new RunRecord(_text.ToString(), _erasedBefore, closure, timing);
        _text.Clear();
        _gaps.Clear();
        _anyStamp = false;
        _erasedBefore = 0;
        Completed?.Invoke(record);
    }

    // The gap since the previous keystroke seen in the span, advancing the span
    // clock. A stampless keystroke (no clock at the caller) contributes 0 and
    // leaves the clock where it was, mirroring the corpus timing rule.
    private int GapSince(double timestampMs)
    {
        if (timestampMs == 0) return 0;
        _anyStamp = true;
        int gap = _lastKeyMs > 0 ? (int)Math.Max(0, Math.Round(timestampMs - _lastKeyMs)) : 0;
        _lastKeyMs = timestampMs;
        return gap;
    }
}
