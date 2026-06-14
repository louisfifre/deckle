using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── Pipeline partial — the post-recording tail of a transcription run.
    //
    // This file is the orchestration seam between the producing strategy
    // (capture + backend, up to Transcribing) and the user-facing delivery.
    // The members themselves now live in two adjacent partials, both in the
    // same Engine/ folder and the same Deckle.Transcription class:
    //   - TranscriptionEngine.Finalize.cs  — FinalizeTranscription + its
    //     clipboard/paste primitives (CopyToClipboard, PasteFromClipboard)
    //     and the LocalizeMicError localizer.
    //   - TranscriptionEngine.Telemetry.cs — the post-recording calibration
    //     and telemetry envelope (TryAutoCalibrate, EmitPreprocessedTelemetry).
}
