using System.Collections.Concurrent;
using Deckle.Core;

namespace Deckle.Autocorrect;

public sealed partial class AutocorrectEngine
{
    private readonly ICaretTextReader? _caretTextReader;
    private readonly ConcurrentQueue<CaretSentenceRecovery> _caretSentenceRecoveries = new();
    private int _caretRecoveryRevision;
    private int _pendingCaretRecoveryRevision = -1;
    private bool _sentenceContextDiscontinuous = true;
    private int _caretRecoveryFailures;
    private bool _caretRecoveryIncidentOpen;

    private void AdvanceCaretRecoveryRevision()
    {
        unchecked { _caretRecoveryRevision++; }
    }

    private void RequestCaretSentenceRecovery()
    {
        if (_caretTextReader is null
            || _coordinator is null)
            return;

        int revision = _caretRecoveryRevision;
        if (_pendingCaretRecoveryRevision == revision) return;
        _pendingCaretRecoveryRevision = revision;

        _ = Task.Run(() =>
        {
            try
            {
                bool succeeded = _caretTextReader.TryReadStable(
                    out FocusedCaretText text,
                    out string reason);
                _caretSentenceRecoveries.Enqueue(
                    new CaretSentenceRecovery(revision, succeeded, text, reason));
            }
            catch (Exception ex)
            {
                // A UI Automation provider can throw from another process. Left
                // unobserved, the fault vanished with the task and the pending
                // revision was never released.
                _caretSentenceRecoveries.Enqueue(new CaretSentenceRecovery(
                    revision, false, default!, CaretReadFaulted) { Fault = ex });
            }
            finally
            {
                _host.RequestDrain();
            }
        });
    }

    private void DrainCaretSentenceRecoveries()
    {
        while (_caretSentenceRecoveries.TryDequeue(out CaretSentenceRecovery recovery))
        {
            if (_pendingCaretRecoveryRevision == recovery.Revision)
                _pendingCaretRecoveryRevision = -1;

            TrackCaretRecoveryFault(recovery.Fault);

            if (recovery.Revision != _caretRecoveryRevision)
            {
                LogCaretRecovery("stale", CaretSentenceBoundary.None, 0);
                continue;
            }
            if (!recovery.Succeeded)
            {
                LogCaretRecovery(recovery.Reason, CaretSentenceBoundary.None, 0);
                continue;
            }

            FocusedSurface surface = _surface;
            AutocorrectSettings settings = _settings();
            if (!settings.Enabled
                || !surface.IsTextEditable
                || surface.IsPassword
                || !IsEnabledFor(settings, surface.ProcessName))
            {
                LogCaretRecovery("surface_gated", CaretSentenceBoundary.None, 0);
                continue;
            }

            CaretSentenceContextResult context = CaretSentenceContext.Extract(
                recovery.Text.TextBeforeCaret,
                recovery.Text.ReachedDocumentStart);
            if (!context.Available
                || !CaretSentenceContext.IsTerminalPunctuation(context.Text[^1]))
            {
                LogCaretRecovery(context.Reason, context.Boundary, 0);
                continue;
            }

            var verified = new VerifiedCaretSentence(recovery.Text, context.Text);
            bool accepted = _coordinator?.RecoverVerifiedSentence(verified) == true;
            LogCaretRecovery(
                accepted ? "accepted" : "unsupported_text",
                context.Boundary,
                context.Text.Length);
        }
    }

    // A provider that throws keeps throwing for as long as the same control has
    // focus, so the faults form one incident: one Warning carrying the first
    // cause, one Informational once a read completes again, and each attempt
    // as the Verbose recovery line. Input thread only.
    private void TrackCaretRecoveryFault(Exception? fault)
    {
        if (fault is null)
        {
            if (_caretRecoveryIncidentOpen)
                DeckleAutocorrectSource.Log.CaretRecoveryRecovered(_caretRecoveryFailures);
            _caretRecoveryIncidentOpen = false;
            _caretRecoveryFailures = 0;
            return;
        }

        _caretRecoveryFailures++;
        if (_caretRecoveryIncidentOpen)
            return;
        _caretRecoveryIncidentOpen = true;
        DeckleAutocorrectSource.Log.CaretRecoveryFailing(
            fault.GetType().FullName ?? fault.GetType().Name, fault.Message);
    }

    private static void LogCaretRecovery(
        string outcome,
        CaretSentenceBoundary boundary,
        int textLength) =>
        DeckleAutocorrectSource.Log.CaretSentenceRecovery(
            outcome,
            boundary.ToString(),
            textLength);

    private const string CaretReadFaulted = "read_faulted";

    private readonly record struct CaretSentenceRecovery(
        int Revision,
        bool Succeeded,
        FocusedCaretText Text,
        string Reason)
    {
        public Exception? Fault { get; init; }
    }
}
