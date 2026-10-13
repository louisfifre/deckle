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
    // Monolithic dictation consumer. Segmented dictation and file transcription
    // share the source-neutral session in StreamingPipeline instead.
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

        // An aborted result can still carry usable segments, so only a failure
        // code without an abort drops the take.
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
