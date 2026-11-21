using Deckle.Autocorrect;
using Deckle.Diagnostics;
using Deckle.Input;
using System.Diagnostics.Tracing;
using System.Text;

namespace Deckle.Autocorrect;

public sealed partial class AutocorrectEngine
{
    private void OnKey(KeyboardKeyEvent e)
    {
        if (e.IsInjected)
        {
            uint deckleTag = unchecked((uint)SendInputInterop.InjectionTag.ToInt64());
            if (e.ExtraInfo == deckleTag)
                return;
            ObserveForeignMutation(e);
            return;
        }

        CompleteForeignMutation();
        if (!_surface.IsTextEditable || _surface.IsPassword) return; // hard gate — before decoding

        var stroke = _decoder.Decode(e);
        if (stroke is null) return;
        var k = stroke.Value;

        _tracker.OnKeystroke(k);
    }

    private void ObserveForeignMutation(KeyboardKeyEvent e)
    {
        const ushort VkBack = 0x08;
        const ushort VkPacket = 0xE7;

        if (!_foreignMutationOpen)
        {
            _foreignMutationOpen = true;

            // Password text is never buffered, even when a synthetic producer
            // exposes its UTF-16 unit in the scan-code field.
            if (!_surface.IsTextEditable || _surface.IsPassword)
                FallBackFromForeignMutation(
                    DeckleAutocorrectSource.ExternalMutationOutcomes.ProtectedSurface);
        }

        if (!e.IsKeyDown || _foreignMutationFallback is not null)
            return;

        if (e.VirtualKey == VkBack && (_foreignReplacement?.Length ?? 0) == 0)
        {
            if (++_foreignBackspaces <= TypedWordTracker.BufferCap + 1)
                return;
            FallBackFromForeignMutation(DeckleAutocorrectSource.ExternalMutationOutcomes.Unsupported);
            return;
        }

        if (e.VirtualKey == VkPacket && e.ScanCode != 0)
        {
            var replacement = _foreignReplacement ??= new StringBuilder();
            if (replacement.Length < TypedWordTracker.BufferCap + 1)
            {
                replacement.Append((char)e.ScanCode);
                return;
            }
        }

        FallBackFromForeignMutation(DeckleAutocorrectSource.ExternalMutationOutcomes.Unsupported);
    }

    private void CompleteForeignMutation()
    {
        if (!_foreignMutationOpen) return;

        string outcome = _foreignMutationFallback
            ?? (_tracker.TryReconcileExternalMutation(
                    _foreignBackspaces, _foreignReplacement?.ToString() ?? string.Empty)
                ? DeckleAutocorrectSource.ExternalMutationOutcomes.Reconciled
                : DeckleAutocorrectSource.ExternalMutationOutcomes.Unmodeled);

        if (outcome == DeckleAutocorrectSource.ExternalMutationOutcomes.Unmodeled)
            _tracker.NotifyExternalMutation();

        LogForeignMutation(outcome);
        ClearForeignMutation();
    }

    private void FallBackFromForeignMutation(string outcome)
    {
        if (_foreignMutationFallback is not null) return;
        _foreignMutationFallback = outcome;
        _tracker.NotifyExternalMutation();
    }

    private void ClearForeignMutation()
    {
        _foreignMutationOpen = false;
        _foreignMutationFallback = null;
        _foreignBackspaces = 0;
        _foreignReplacement?.Clear();
    }

    private void LogForeignMutation(string outcome)
    {
        if (!DeckleAutocorrectSource.IsActivityDetailEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            return;
        DeckleAutocorrectSource.Log.ExternalMutationBurst(
            outcome, _foreignBackspaces, _foreignReplacement?.Length ?? 0);
    }

    private void OnPointerInteraction()
    {
        ClearForeignMutation();
        _tracker.NotifyPointerInteraction();
    }

    private void OnFocusChanged()
    {
        ClearForeignMutation();
        var surface = _prober.Probe();
        _tracker.NotifyFocusChanged();
        _surface = surface;

        bool enabled = IsEnabledFor(_settings(), surface.ProcessName);
        DeckleAutocorrectSource.Log.SurfaceChanged(
            surface.ProcessName, surface.IsTextEditable, surface.IsPassword, enabled, surface.Probe);
        SurfaceChanged?.Invoke(surface, enabled);
    }
}
