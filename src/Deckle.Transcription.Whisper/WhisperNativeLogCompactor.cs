// WhisperNativeLogCompactor — init-phase compaction state machine for whisper.cpp native logs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Deckle.Core;
using Deckle.Transcription;
using Deckle.Transcription.Engine;
using Deckle.Transcription.Whisper.Engine;
using Deckle.Transcription.Whisper.Pinvoke;
using Deckle.Transcription.Whisper.Setup;

namespace Deckle.Transcription.Whisper;

// ── Init-phase compaction ────────────────────────────────────────────────────
//
// whisper.cpp's init flow emits four distinct prefix groups whose lines
// are useful as a whole but noisy individually. We accumulate the
// value-of-interest from each line of the active phase into per-phase
// state, and flush a single consolidated event the moment we see a line
// from a different phase (or any non-phase line). The fifth init prefix —
// whisper_init_from_file_with_params_no_state: — emits a single line and
// passes through unchanged.
//
// Pure string state machine: no dependency on _ctx, the model, or any native
// pointer. Owned by WhisperBackend and driven from its native log hook.
internal sealed class WhisperNativeLogCompactor
{
    private static readonly string[] s_phasePrefixes = new[]
    {
        "whisper_init_with_params_no_state:",
        "whisper_model_load:",
        "whisper_backend_init_gpu:",
        "whisper_init_state:",
    };

    // Current phase index (0..3) and accumulator. -1 means no phase active.
    private int _phaseIndex = -1;
    private readonly List<string> _phaseAccumulator = new();

    // Returns true when the line was consumed by the phase machinery (and so
    // must not flow through the normal level switch in the log hook).
    public bool TryAccumulatePhaseLine(string msg)
    {
        int matched = -1;
        for (int i = 0; i < s_phasePrefixes.Length; i++)
        {
            if (msg.StartsWith(s_phasePrefixes[i], StringComparison.Ordinal))
            {
                matched = i;
                break;
            }
        }

        if (matched < 0)
        {
            // Non-phase line — flush any pending phase first, then let the
            // caller route the line normally.
            FlushPendingPhase();
            return false;
        }

        if (_phaseIndex >= 0 && matched != _phaseIndex)
        {
            // Different phase started — flush the previous one before
            // starting accumulation on the new phase.
            FlushPendingPhase();
        }

        _phaseIndex = matched;
        // Capture the substring after the prefix, trimmed. Empty bodies are
        // skipped — they carry no value to consolidate.
        string body = msg.Substring(s_phasePrefixes[matched].Length).Trim();
        if (body.Length > 0) _phaseAccumulator.Add(body);
        return true;
    }

    public void FlushPendingPhase()
    {
        if (_phaseIndex < 0) return;
        string consolidated = string.Join(" | ", _phaseAccumulator);
        switch (_phaseIndex)
        {
            case 0: DeckleWhispSource.Log.WhisperInitParamsParsed(consolidated); break;
            case 1: DeckleWhispSource.Log.WhisperModelLoadParsed(consolidated); break;
            case 2: DeckleWhispSource.Log.WhisperBackendInitParsed(consolidated); break;
            case 3: DeckleWhispSource.Log.WhisperInitStateParsed(consolidated); break;
        }
        _phaseAccumulator.Clear();
        _phaseIndex = -1;
    }
}
