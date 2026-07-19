using System.Runtime.InteropServices;
using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Input;

namespace Deckle.Input;

public sealed partial class KeyboardInputHost
{
    private static bool IsKeyboardRollupEnabled()
        => DeckleInputSource.IsAutocorrectDetailEnabled(
            EventLevel.Verbose,
            (EventKeywords)Keywords.Heartbeat);

    private void TrackRollup(double nowMs)
    {
        // This supporting provider belongs to Autocorrect while that activity
        // is running. Refuse the rollup at the producer so its counters and
        // EventSource payload do no work when activity detail is disabled.
        if (!IsKeyboardRollupEnabled())
        {
            if (_rollupStartMs >= 0)
                ResetRollup(nowMs: -1);
            return;
        }

        if (_rollupStartMs < 0) _rollupStartMs = nowMs;

        if (nowMs - _rollupStartMs < RollupPeriodMs) return;

        DeckleInputSource.Log.KeyboardRollup(
            _rollupKeys, _rollupInjectedFiltered, _rollupPointerDowns, _rollupWheel, _rollupFocusChanges);

        ResetRollup(nowMs);
    }

    private void ResetRollup(double nowMs)
    {
        _rollupStartMs = nowMs;
        _rollupKeys = 0;
        _rollupInjectedFiltered = 0;
        _rollupPointerDowns = 0;
        _rollupWheel = 0;
        _rollupFocusChanges = 0;
    }
}
