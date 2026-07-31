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

    // Shared monolithic consumer. Dictation and file transcription have
    // different producers (microphone capture vs. file decode), but both hand
    // the resulting 16-kHz mono PCM to this exact backend boundary. Source-
    // specific policy stays outside: dictation may keep an aborted partial
    // result, while a durable file transcript rejects it before delivery.
    private async Task<TranscriptionResult?> ConsumeMonolithicAudioAsync(
        ReadOnlyMemory<float> audio,
        CancellationToken cancellationToken,
        TranscriptionContext? context = null)
    {
        TranscriptionResult result;
        try
        {
            result = await _backend.TranscribeAsync(
                audio,
                segment => NewSegment?.Invoke(segment),
                cancellationToken,
                context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                DeckleCancellationSource.Log.OperationCancelled(
                    "whisp-transcribe", "upstream", -1);
            }

            DeckleWhispSource.Log.TranscribeFailed();
            DeckleWhispSource.Log.TranscribeFailedDetail(-1);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_TranscriptionFailed_Title"),
                Loc.Get("Engine_TranscriptionFailed_Body"),
                FB_REPLACEMENT);
            RaiseStatus(Loc.Get("Status_TranscriptionFailed"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        // Preserve the live pipeline's established partial-result policy. An
        // aborted result can still contain usable segments; the file producer
        // applies its stricter durable-output gate after this shared consumer.
        if (result.ResultCode != 0 && !result.Aborted)
        {
            DeckleWhispSource.Log.TranscribeFailed();
            DeckleWhispSource.Log.TranscribeFailedDetail(result.ResultCode);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_TranscriptionFailed_Title"),
                Loc.Get("Engine_TranscriptionFailed_Body"),
                FB_REPLACEMENT);
            RaiseStatus(Loc.Get("Status_TranscriptionFailed"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        return result;
    }
}
