using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    private void RecordPipelineMetrics(
        PipelineProduction production,
        string rawText,
        FinalizeRewrite rewrite,
        FinalizeDelivery delivery,
        long clipboardMs,
        bool isFileRun,
        double recordingDurationSec)
    {
        int finalWordCount = TextMetrics.CountWords(rewrite.Text);
        long hotkeyToCaptureMs = _hotkeySw?.ElapsedMilliseconds ?? 0;
        long recordDrainMs = (long)_recordDrainDuration.TotalMilliseconds;
        long stopToPipelineMs = _stopToPipelineSw?.ElapsedMilliseconds ?? 0;
        long whisperInitMs = production.InitMs;
        long whisperMs = Math.Max(0, production.TotalTranscribeMs - whisperInitMs);
        string strategyLabel = _backend.Name;

        if (isFileRun)
        {
            if (delivery.Outcome == TranscriptionOutcome.SavedToFile)
                DeckleWhispSource.Log.FileTranscriptionCompleted();
            else
                DeckleWhispSource.Log.FileTranscriptionCopied();
        }
        else if (delivery.Outcome == TranscriptionOutcome.Pasted)
        {
            DeckleWhispSource.Log.DictationPasted();
        }
        else
        {
            DeckleWhispSource.Log.DictationCopied();
        }

        DeckleWhispSource.Log.PipelineCompletedDetail(delivery.Outcome.ToString());
        DeckleWhispSource.Log.PipelineTimings(
            recordingDurationSec, _modelLoadMs, hotkeyToCaptureMs, recordDrainMs,
            stopToPipelineMs, whisperInitMs, whisperMs, rewrite.LlmMs,
            clipboardMs, delivery.PasteMs);
        DeckleWhispSource.Log.PipelineLlmMetrics(
            rewrite.OllamaLoadMs,
            rewrite.LlmPromptEvalMs,
            rewrite.LlmEvalMs,
            rewrite.LlmPromptTokens,
            rewrite.LlmEvalTokens);
        DeckleWhispSource.Log.PipelineOutputs(
            production.NSegments,
            rewrite.Text.Length,
            finalWordCount,
            strategyLabel,
            rewrite.Profile?.Name ?? "(none)",
            delivery.Outcome.ToString());

        _recordingSw?.Stop();

        if (!isFileRun)
        {
            float audioSec = (float)production.RawAudio.Length / 16_000f;
            DeckleWhispSource.Log.LatencyRecorded(
                transcription_id:     _transcriptionId,
                audio_sec:            audioSec,
                model_load_ms:        _modelLoadMs,
                hotkey_to_capture_ms: hotkeyToCaptureMs,
                record_drain_ms:      recordDrainMs,
                stop_to_pipeline_ms:  stopToPipelineMs,
                whisper_init_ms:      whisperInitMs,
                whisper_ms:           whisperMs,
                llm_ms:               rewrite.LlmMs,
                ollama_load_ms:       rewrite.OllamaLoadMs,
                llm_prompt_eval_ms:   rewrite.LlmPromptEvalMs,
                llm_eval_ms:          rewrite.LlmEvalMs,
                llm_prompt_tokens:    rewrite.LlmPromptTokens,
                llm_eval_tokens:      rewrite.LlmEvalTokens,
                clipboard_ms:         clipboardMs,
                paste_ms:             delivery.PasteMs,
                strategy:             strategyLabel,
                n_segments:           production.NSegments,
                text_chars:           rewrite.Text.Length,
                text_words:           finalWordCount,
                profile:              rewrite.Profile?.Name ?? "",
                pasted:               delivery.PasteVerified,
                outcome:              delivery.Outcome.ToString());
        }

        RecordCorpus(production, rawText, rewrite, isFileRun, recordingDurationSec, whisperMs);
    }

    private void RecordCorpus(
        PipelineProduction production,
        string rawText,
        FinalizeRewrite rewrite,
        bool isFileRun,
        double recordingDurationSec,
        long whisperMs)
    {
        var telemetrySettings = _host.Telemetry;
        if (!telemetrySettings.CorpusEnabled || isFileRun)
            return;

        var asrSettings = _host.Transcription.Engine;
        int rawWordCount = TextMetrics.CountWords(rawText);
        string asrTier = CorpusTier.Resolve(rawWordCount);

        ReadOnlyMemory<float> corpusAudioMemory =
            telemetrySettings.AudioCorpusContent == AudioCorpusContent.AlwaysRaw
                ? production.RawAudio
                : production.BackendAudio;
        string corpusContent = !corpusAudioMemory.Equals(production.RawAudio) ? "processed" : "raw";
        float[] corpusAudio = MemoryMarshal.TryGetArray(corpusAudioMemory, out ArraySegment<float> segment)
            && segment.Offset == 0
            && segment.Count == segment.Array!.Length
                ? segment.Array
                : corpusAudioMemory.ToArray();
        string audioFileName = telemetrySettings.RecordAudioCorpus
            ? (WavCorpusWriter.Write(_transcriptionId, corpusAudio) ?? "")
            : "";

        DeckleWhispSource.Log.CorpusAsrRecorded(
            transcription_id:      _transcriptionId,
            audio_file:            audioFileName,
            bucket:                "raw",
            tier:                  asrTier,
            backend:               _backend.Name,
            model:                 asrSettings.Model,
            language:              asrSettings.Language,
            prompt_or_instruction: asrSettings.InitialPrompt ?? "",
            text:                  rawText,
            text_words:            rawWordCount,
            text_chars:            rawText.Length,
            duration_seconds:      recordingDurationSec,
            words_per_second:      recordingDurationSec > 0 ? rawWordCount / recordingDurationSec : 0,
            elapsed_ms:            whisperMs,
            audio_content:         corpusContent);

        if (rewrite.Profile is null)
            return;

        int rewriteWordCount = TextMetrics.CountWords(rewrite.Text);
        string rewriteBucket = CorpusPaths.Sanitize(
            $"rewrite-{CorpusPaths.Slugify(rewrite.Profile.Name)}-{rewrite.Profile.Id}");
        DeckleWhispSource.Log.CorpusRewriteRecorded(
            transcription_id:      _transcriptionId,
            audio_file:            audioFileName,
            bucket:                rewriteBucket,
            rewrite_profile_id:    rewrite.Profile.Id,
            rewrite_profile_name:  rewrite.Profile.Name,
            ollama_endpoint:       _host.Llm.OllamaEndpoint,
            ollama_model:          rewrite.Profile.Model ?? "",
            prompt_template_hash:  PromptTemplateHash.Of(rewrite.Profile),
            text:                  rewrite.Text,
            text_words:            rewriteWordCount,
            text_chars:            rewrite.Text.Length,
            elapsed_ms:            rewrite.LlmMs);
    }
}
