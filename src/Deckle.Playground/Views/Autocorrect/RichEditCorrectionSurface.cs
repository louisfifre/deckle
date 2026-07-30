using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Playground;

// Phase-A adapter for the Playground-owned RichEditBox. The tracked sentence
// range is diagnostic only; every write is performed through a fresh absolute
// range after exact body, sentence, and token checks.
internal sealed class RichEditCorrectionSurface
{
    private readonly RichEditBox _editor;
    private ITextRange? _diagnosticSentence;

    public RichEditCorrectionSurface(RichEditBox editor)
    {
        _editor = editor;
    }

    public int Generation { get; private set; }

    public bool Reset(string body)
    {
        Generation = checked(Generation + 1);
        _diagnosticSentence = null;

        try
        {
            _editor.TextDocument.SetText(TextSetOptions.None, body);
            var selection = _editor.TextDocument.Selection;
            selection.SetRange(body.Length, body.Length);
            return TryReadBody(out string observed, out bool mapping)
                && mapping
                && string.Equals(observed, body, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public bool ArmDiagnosticSentence(int start, int length, string literal)
    {
        try
        {
            var range = _editor.TextDocument.GetRange(start, checked(start + length));
            range.Gravity = RangeGravity.Inward;
            range.GetText(TextGetOptions.None, out string observed);
            if (!string.Equals(observed, literal, StringComparison.Ordinal))
            {
                _diagnosticSentence = null;
                return false;
            }

            _diagnosticSentence = range;
            return true;
        }
        catch
        {
            _diagnosticSentence = null;
            return false;
        }
    }

    public void ClearDiagnosticSentence()
        => _diagnosticSentence = null;

    public bool TrySnapshot(
        CorrectionApplicationEdit edit,
        string sentenceLiteral,
        out CorrectionSurfaceSnapshot snapshot)
    {
        snapshot = default;

        try
        {
            if (!TryReadBody(out string body, out bool mapping))
            {
                return false;
            }

            mapping = mapping
                && HasExactPrefixMapping(body, edit.Start)
                && HasExactPrefixMapping(body, edit.End);

            var selection = _editor.TextDocument.Selection;
            var selectionSnapshot = new CorrectionApplicationSelection(
                selection.StartPosition,
                selection.EndPosition,
                (int)selection.Options);

            bool diagnosticExact = false;
            if (_diagnosticSentence is not null)
            {
                _diagnosticSentence.GetText(TextGetOptions.None, out string diagnostic);
                diagnosticExact = string.Equals(
                    diagnostic,
                    sentenceLiteral,
                    StringComparison.Ordinal);
            }

            bool targetExact = false;
            if (edit.Start >= 0 && edit.End <= body.Length)
            {
                var target = _editor.TextDocument.GetRange(edit.Start, edit.End);
                target.GetText(TextGetOptions.None, out string observedTarget);
                targetExact = target.Length == edit.Length
                    && string.Equals(
                    observedTarget,
                    edit.Literal,
                    StringComparison.Ordinal);
            }

            snapshot = new CorrectionSurfaceSnapshot(
                body,
                selectionSnapshot,
                mapping,
                diagnosticExact,
                targetExact);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public CorrectionSurfaceExecution Execute(
        CorrectionApplicationPlan plan,
        CorrectionSurfaceFault fault)
    {
        var document = _editor.TextDocument;
        bool writeAttempted = false;
        bool? exactAppliedText = null;
        bool? exactAppliedSelection = null;
        bool? exactUndoText = null;
        bool? exactUndoSelection = null;
        bool? exactRedoText = null;
        bool? exactRedoSelection = null;

        CorrectionSurfaceExecution Finish(CorrectionApplicationReason reason)
            => new(
                reason,
                writeAttempted,
                exactAppliedText,
                exactAppliedSelection,
                exactUndoText,
                exactUndoSelection,
                exactRedoText,
                exactRedoSelection);

        try
        {
            if (fault == CorrectionSurfaceFault.FreshRangeBeforeWrite)
            {
                return Finish(CorrectionApplicationReason.TargetRangeChanged);
            }

            // This is the final authority check. The diagnostic tracked range is
            // intentionally not reused as the write target.
            var target = document.GetRange(plan.Edit.Start, plan.Edit.End);
            target.GetText(TextGetOptions.None, out string observedTarget);
            if (!string.Equals(observedTarget, plan.Edit.Literal, StringComparison.Ordinal))
            {
                return Finish(CorrectionApplicationReason.TargetRangeChanged);
            }

            document.BeginUndoGroup();
            try
            {
                writeAttempted = true;
                target.SetText(TextSetOptions.None, plan.Edit.Replacement);
            }
            finally
            {
                document.EndUndoGroup();
            }

            if (!TryReadBodyAndSelection(out string appliedBody, out var appliedSelection))
            {
                return Finish(CorrectionApplicationReason.ApiFailure);
            }

            exactAppliedText = string.Equals(
                    appliedBody,
                    plan.ExpectedBody,
                    StringComparison.Ordinal)
                && fault != CorrectionSurfaceFault.AppliedTextPostcondition;
            if (exactAppliedText != true)
            {
                return Finish(CorrectionApplicationReason.TextPostcondition);
            }

            exactAppliedSelection = appliedSelection == plan.ExpectedSelection
                && fault != CorrectionSurfaceFault.AppliedSelectionPostcondition;
            if (exactAppliedSelection != true)
            {
                return Finish(CorrectionApplicationReason.SelectionPostcondition);
            }

            document.Undo();
            if (!TryReadBodyAndSelection(out string undoBody, out var undoSelection))
            {
                return Finish(CorrectionApplicationReason.ApiFailure);
            }

            exactUndoText = string.Equals(
                    undoBody,
                    plan.BeforeBody,
                    StringComparison.Ordinal)
                && fault != CorrectionSurfaceFault.UndoTextPostcondition;
            if (exactUndoText != true)
            {
                return Finish(CorrectionApplicationReason.UndoTextPostcondition);
            }

            exactUndoSelection = undoSelection == plan.BeforeSelection
                && fault != CorrectionSurfaceFault.UndoSelectionPostcondition;
            if (exactUndoSelection != true)
            {
                return Finish(CorrectionApplicationReason.UndoSelectionPostcondition);
            }

            document.Redo();
            if (!TryReadBodyAndSelection(out string redoBody, out var redoSelection))
            {
                return Finish(CorrectionApplicationReason.ApiFailure);
            }

            exactRedoText = string.Equals(
                    redoBody,
                    plan.ExpectedBody,
                    StringComparison.Ordinal)
                && fault != CorrectionSurfaceFault.RedoTextPostcondition;
            if (exactRedoText != true)
            {
                return Finish(CorrectionApplicationReason.RedoTextPostcondition);
            }

            exactRedoSelection = redoSelection == plan.ExpectedSelection
                && fault != CorrectionSurfaceFault.RedoSelectionPostcondition;
            return Finish(exactRedoSelection == true
                ? CorrectionApplicationReason.None
                : CorrectionApplicationReason.RedoSelectionPostcondition);
        }
        catch
        {
            // No rollback after an uncertain write: the lab reports an integrity
            // failure and leaves the exact observed state available for inspection.
            return Finish(CorrectionApplicationReason.ApiFailure);
        }
    }

    public bool TryObserve(
        out int bodyLength,
        out CorrectionApplicationSelection selection,
        out bool isExactMapping)
    {
        bodyLength = 0;
        selection = default;
        isExactMapping = false;

        try
        {
            if (!TryReadBody(out string body, out isExactMapping))
            {
                return false;
            }

            var current = _editor.TextDocument.Selection;
            bodyLength = body.Length;
            selection = new CorrectionApplicationSelection(
                current.StartPosition,
                current.EndPosition,
                (int)current.Options);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadBodyAndSelection(
        out string body,
        out CorrectionApplicationSelection selectionSnapshot)
    {
        selectionSnapshot = default;
        if (!TryReadBody(out body, out bool mapping) || !mapping)
        {
            return false;
        }

        var selection = _editor.TextDocument.Selection;
        selectionSnapshot = new CorrectionApplicationSelection(
            selection.StartPosition,
            selection.EndPosition,
            (int)selection.Options);
        return true;
    }

    private bool TryReadBody(out string body, out bool isExactMapping)
    {
        body = string.Empty;
        isExactMapping = false;

        var selection = _editor.TextDocument.Selection;
        int storyLength = selection.StoryLength;
        if (storyLength < 1)
        {
            return false;
        }

        int bodyEnd = storyLength - 1;
        var bodyRange = _editor.TextDocument.GetRange(0, bodyEnd);
        bodyRange.GetText(TextGetOptions.None, out body);

        // The final end-of-paragraph marker belongs to TOM, not to the .NET
        // body. Validate it separately; never trim a guessed character.
        var finalEopRange = _editor.TextDocument.GetRange(bodyEnd, storyLength);
        finalEopRange.GetText(TextGetOptions.None, out string finalEop);
        isExactMapping = bodyRange.Length == bodyEnd
            && body.Length == bodyEnd
            && finalEopRange.Length == 1
            && finalEop.Length == 1;
        return true;
    }

    private bool HasExactPrefixMapping(string body, int end)
    {
        if (end < 0 || end > body.Length)
        {
            return false;
        }

        var prefixRange = _editor.TextDocument.GetRange(0, end);
        prefixRange.GetText(TextGetOptions.None, out string prefix);
        return prefixRange.Length == end
            && prefix.Length == end
            && body.AsSpan(0, end).Equals(prefix.AsSpan(), StringComparison.Ordinal);
    }
}

internal readonly record struct CorrectionSurfaceSnapshot(
    string Body,
    CorrectionApplicationSelection Selection,
    bool IsTomMappingExact,
    bool IsDiagnosticSentenceExact,
    bool IsTargetRangeExact);
