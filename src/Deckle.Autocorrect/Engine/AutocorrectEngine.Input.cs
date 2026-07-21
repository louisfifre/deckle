using Deckle.Autocorrect;
using Deckle.Diagnostics;
using Deckle.Input;
using System.Diagnostics.Tracing;

namespace Deckle.Autocorrect;

public sealed partial class AutocorrectEngine
{
    // ── Input thread handlers ────────────────────────────────────────────

    // Called by the host when telemetry settings change. RequestDrain is the
    // cross-thread marshalling point; the corpus itself remains input-thread-owned.
    public void ReconcileTextTelemetry()
    {
        if (_textTelemetry?.Invoke() == true) return;
        Interlocked.Exchange(ref _discardCorpusRequested, 1);
        _host.RequestDrain();
    }

    private void OnDrainRequested()
    {
        if (Interlocked.Exchange(ref _discardCorpusRequested, 0) != 0)
        {
            _corpus?.Discard();
            _stream?.Discard();
        }
    }

    private void OnKey(KeyboardKeyEvent e)
    {
        if (e.IsInjected) return;            // our own repairs never feed the view
        if (!_surface.IsTextEditable || _surface.IsPassword) return; // hard gate — before decoding

        var stroke = _decoder.Decode(e);
        if (stroke is null) return;
        var k = stroke.Value;

        // The coordinator sees the live word as it stood BEFORE the tracker
        // consumes this stroke — so a Backspace into committed text invalidates its
        // model. Resets proper arrive via OnTrackerReset.
        _coordinator?.NotePhysicalKey(k, _tracker.HasCurrentWord);

        // The typing stream captures the verbatim stroke before the tracker
        // interprets it. Gated per stroke — enrolled surfaces only, consent live;
        // the password gate already cut above, before decoding.
        if (ShouldFeedStream())
            _stream?.OnKeystroke(k);

        _tracker.OnKeystroke(k);
    }

    private void OnPointerInteraction()
    {
        _tracker.NotifyPointerInteraction();
        // Ungated: a span must close on every caret move, or a stale run would
        // leak into whatever surface is typed next.
        _stream?.NotifyPointerInteraction();
    }

    // The tracker reset (Enter, focus, pointer, navigation, …) clears the sentence
    // model. No contextual verdict may survive a discontinuity.
    private void OnTrackerReset(ResetReason reason, bool droppedPartialWord)
    {
        // Close the corpus sentence first (Enter emits it tagged "enter", any other
        // reason emits the partial run tagged "interrupted" — still verbatim keyboard
        // input); the emit is gated downstream by the sink, so a flip to off between
        // accumulation and reset cannot leak a sentence to disk.
        _corpus?.Reset(reason);
        // A reset that threw away a word in flight can leave its tail to commit as
        // the next "word" — a fragment that used to pollute corpus sentence starts
        // (« e Setting UX … »). The corpus holds that next word suspect and drops it.
        if (droppedPartialWord)
            _corpus?.MarkNextWordSuspect();
        _coordinator?.Invalidate(reason);
    }

    // A correction the contextual stage applied inside the closed sentence. It
    // counts and logs like any correction, and records the actual write plan.
    private void OnCoordinatorApplied(CorrectionDecision decision, InjectionPlan plan)
    {
        if (IsActivityRollupEnabled()) _rollupCorrections++;
        DeckleAutocorrectSource.Log.CorrectionApplied();
        LogCorrectionDetail(decision, plan.Backspaces);
        if (CanCollectText())
            _corpus?.SentenceEdit(decision.Original, decision.Replacement);
        CorrectionApplied?.Invoke(decision);
    }

    private void OnFocusChanged()
    {
        var surface = _prober.Probe();
        // The reset synchronously closes the corpus run and the typing-stream
        // span. Keep the producing surface live until both have been emitted;
        // only then publish the newly focused surface.
        _tracker.NotifyFocusChanged();
        _stream?.NotifyFocusChanged();
        _surface = surface;

        bool enabled = IsEnabledFor(_settings(), surface.ProcessName);
        DeckleAutocorrectSource.Log.SurfaceChanged(
            surface.ProcessName, surface.IsTextEditable, surface.IsPassword, enabled, surface.Probe);
        SurfaceChanged?.Invoke(surface, enabled);
    }
}
