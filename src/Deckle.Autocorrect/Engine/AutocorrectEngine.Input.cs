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
        if (Interlocked.Exchange(ref _pausePassRequested, 0) != 0)
            MaybeRunPausePass();
    }

    // The timer's threadpool callback: raise the flag and marshal to the input
    // thread — never touch the coordinator from here.
    private void OnPauseTimerElapsed(object? _)
    {
        if (_pauseThresholdMs <= 0) return;
        Interlocked.Exchange(ref _pausePassRequested, 1);
        _host.RequestDrain();
    }

    // Input thread. The timer races the keyboard: a key landing after the arm
    // re-arms it, but a fire may already be in flight — re-check the clock so a
    // pause is only declared when nothing was typed for the threshold (small
    // slack for timer granularity).
    private void MaybeRunPausePass()
    {
        int threshold = _pauseThresholdMs;
        if (threshold <= 0 || _coordinator is null) return;
        if (Environment.TickCount64 - _lastKeyTickMs < threshold - 50) return;

        int slots = _coordinator.FlushOnPause();
        if (slots > 0)
            DeckleAutocorrectSource.Log.PausePassTriggered(threshold, slots);
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

        // Re-arm the pause clock on every physical key. Armed only where the
        // surface's profile set a bar — everywhere else the timer never runs.
        _lastKeyTickMs = Environment.TickCount64;
        int pauseMs = _pauseThresholdMs;
        if (pauseMs > 0)
            _pauseTimer?.Change(pauseMs, Timeout.Infinite);
    }

    private void OnPointerInteraction()
    {
        _tracker.NotifyPointerInteraction();
        // Ungated: a span must close on every caret move, or a stale run would
        // leak into whatever surface is typed next.
        _stream?.NotifyPointerInteraction();
    }

    // The tracker reset (Enter, focus, pointer, navigation, …) clears the sentence
    // model. Enter is forwarded verbatim so the coordinator can vouch the next word
    // as sentence-initial; every other reason is a caret move to an unknown spot.
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

    // A correction the contextual stage applied behind the caret. It counts and
    // logs like any correction, and records a Sentence transition on the corpus
    // slot if the sentence is still open (a rewrite after flush is invisible).
    private void OnCoordinatorApplied(CorrectionDecision decision)
    {
        if (IsActivityRollupEnabled()) _rollupCorrections++;
        DeckleAutocorrectSource.Log.CorrectionApplied();
        LogCorrectionDetail(decision, backspaces: 0);
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

        // The pause pass follows the surface: armed at its measured bar where
        // the profile qualifies, disarmed (threshold 0) everywhere else.
        _pauseThresholdMs = _profiles is not null
            && _profiles.TryGetValue(surface.ProcessName, out SurfaceProfileRecord? profile)
            ? profile.PauseThresholdMs
            : 0;
        if (_pauseThresholdMs == 0)
            _pauseTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        bool enabled = IsEnabledFor(_settings(), surface.ProcessName);
        DeckleAutocorrectSource.Log.SurfaceChanged(
            surface.ProcessName, surface.IsTextEditable, surface.IsPassword, enabled, surface.Probe);
        SurfaceChanged?.Invoke(surface, enabled);
    }
}
