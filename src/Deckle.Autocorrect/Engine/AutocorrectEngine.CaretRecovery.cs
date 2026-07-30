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
            bool succeeded = _caretTextReader.TryReadStable(
                out FocusedCaretText text,
                out string reason);
            _caretSentenceRecoveries.Enqueue(
                new CaretSentenceRecovery(revision, succeeded, text, reason));
            _host.RequestDrain();
        });
    }

    private void DrainCaretSentenceRecoveries()
    {
        while (_caretSentenceRecoveries.TryDequeue(out CaretSentenceRecovery recovery))
        {
            if (_pendingCaretRecoveryRevision == recovery.Revision)
                _pendingCaretRecoveryRevision = -1;

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

    private static void LogCaretRecovery(
        string outcome,
        CaretSentenceBoundary boundary,
        int textLength) =>
        DeckleAutocorrectSource.Log.CaretSentenceRecovery(
            outcome,
            boundary.ToString(),
            textLength);

    private readonly record struct CaretSentenceRecovery(
        int Revision,
        bool Succeeded,
        FocusedCaretText Text,
        string Reason);
}
