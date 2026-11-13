using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Transcription;

// Closed `reason` vocabulary of CorpusRecordSkipped.
internal static class CorpusSkipReasons
{
    public const string CorpusDisabled = "corpus_disabled";
    public const string FileRun = "file_run";
    public const string NoProfile = "no_profile";
}

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
        }
        else
        {
            DeckleWhispSource.Log.DictationCopied();
        }

        DeckleWhispSource.Log.PipelineCompletedDetail(delivery.Outcome.ToString());
        DeckleWhispSource.Log.PipelineTimings(
            recordingDurationSec, _modelLoadMs, hotkeyToCaptureMs, recordDrainMs,
            stopToPipelineMs, whisperInitMs, whisperMs, rewrite.LlmMs,
            clipboardMs);
        DeckleWhispSource.Log.PipelineLlmMetrics(
            rewrite.LlmLoadMs,
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
        if (!telemetrySettings.CorpusEnabled)
        {
            DeckleWhispSource.Log.CorpusRecordSkipped(CorpusSkipReasons.CorpusDisabled);
            return;
        }
        if (isFileRun)
        {
            DeckleWhispSource.Log.CorpusRecordSkipped(CorpusSkipReasons.FileRun);
            return;
        }

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
        {
            DeckleWhispSource.Log.CorpusRecordSkipped(CorpusSkipReasons.NoProfile);
            return;
        }

        int rewriteWordCount = TextMetrics.CountWords(rewrite.Text);
        string rewriteBucket = CorpusPaths.Sanitize(
            $"rewrite-{CorpusPaths.Slugify(rewrite.Profile.Name)}-{rewrite.Profile.Id}");
        DeckleWhispSource.Log.CorpusRewriteRecorded(
            transcription_id:      _transcriptionId,
            audio_file:            audioFileName,
            bucket:                rewriteBucket,
            rewrite_profile_id:    rewrite.Profile.Id,
            rewrite_profile_name:  rewrite.Profile.Name,
            // The event fields keep their Ollama-era names (ETW manifest); the
            // values are the profile's provider endpoint and model.
            ollama_endpoint:       _host.Llm.Providers.Find(p => p.Id == rewrite.Profile.ProviderId)?.Endpoint ?? "",
            ollama_model:          rewrite.Profile.Model ?? "",
            prompt_template_hash:  PromptTemplateHash.Of(rewrite.Profile),
            text:                  rewrite.Text,
            text_words:            rewriteWordCount,
            text_chars:            rewrite.Text.Length,
            elapsed_ms:            rewrite.LlmMs);
    }
}
