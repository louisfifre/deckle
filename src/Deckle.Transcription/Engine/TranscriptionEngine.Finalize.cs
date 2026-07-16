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
    // ── Finalize partial — delivery tail (finalize + clipboard + paste) and the mic-error localizer ──

    // ── Microphone error localization ──────────────────────────────────────────
    //
    // MicErrorKind → (title, body) for UI. Messages formulated for the end user
    // — no Win32 jargon. Raw MMSYSERR code is included verbatim in the
    // Unavailable_Body_Format path so users can paste it back when reporting.
    // Capture itself stays free of any Loc.Get dependency; the engine owns
    // the localization step.
    private static (string Title, string Body) LocalizeMicError(MicErrorKind kind, uint err) => kind switch
    {
        MicErrorKind.NotDetected => (Loc.Get("MicError_NotDetected_Title"), Loc.Get("MicError_NotDetected_Body")),
        MicErrorKind.InUse       => (Loc.Get("MicError_InUse_Title"),       Loc.Get("MicError_InUse_Body")),
        _                        => (Loc.Get("MicError_Unavailable_Title"), Loc.Format("MicError_Unavailable_Body_Format", err)),
    };


    // ── Shared finalize ──────────────────────────────────────────────────────
    //
    // Strategy-agnostic tail of a recording. From the assembled raw text plus
    // the captured audio it writes the clipboard once, resolves and applies an
    // optional LLM rewrite, optionally pastes, then emits the latency + corpus
    // telemetry and raises Finished. Both pipelines — monolithic and streaming —
    // converge here, so the user-facing behaviour is identical whatever produced
    // the text. Synchronous: every step (clipboard, rewrite, paste) is blocking.
    //
    // The producing strategy owns capture, the backend call(s), and the state
    // transitions up to Transcribing; here we only consume the result it hands
    // back. _transcriptionId is generated once per recording by WorkerRun before
    // the strategy runs under the corpus join contract.
    private void FinalizeTranscription(PipelineProduction prod)
    {
        string  fullText          = prod.RawText;
        ReadOnlyMemory<float> audio        = prod.RawAudio;
        ReadOnlyMemory<float> backendAudio = prod.BackendAudio;
        float   audioSec          = (float)audio.Length / 16_000f;
        long    transcribeMsTotal = prod.TotalTranscribeMs;
        int     nSeg              = prod.NSegments;

        // A file-transcription run: the tail diverges from dictation in three
        // places below — no rewrite (verbatim, V1), delivery is a .txt on disk
        // (plus the clipboard) instead of a paste, and the voice corpus is not
        // fed. _fileTranscriptionPath is the source audio path, set by
        // RequestFileTranscription and cleared by TryStartFromIdle.
        bool isFileRun = _fileTranscriptionPath is not null;

        // Low-audio warning is emitted live by MicrophoneCapture once 5 s
        // of sustained sub-threshold signal has accumulated — see the
        // tracker in WaveInLoop.Pump and the OnCaptureLowAudioDetected
        // localizer above. Alerting during recording is the whole point
        // of that message: we want the user to stop talking into a broken
        // mic within seconds, not discover it 20 min later.

        // Always copy raw text first — safety net even if LLM fails. If the
        // copy fails (all three CopyToClipboard error paths already emit a
        // Critical UserFeedback), short-circuit: paste would send Ctrl+V into
        // an empty clipboard, which in most apps pastes whatever was there
        // before the transcription — confusing at best. Better to stop here.
        var swClip = System.Diagnostics.Stopwatch.StartNew();
        bool rawCopyOk = CopyToClipboard(fullText);
        swClip.Stop();
        if (!rawCopyOk)
        {
            // On a file run the .txt is the primary deliverable: a transient
            // clipboard failure (another process holding it open) must not
            // throw away minutes of decode + inference. Write the file anyway,
            // but keep the outcome None so the Critical clipboard feedback
            // stays visible — SavedToFile would replace it with a success
            // banner whose "also copied" hint would be false, inviting a paste
            // of stale clipboard content. The transcript is on disk either way.
            if (isFileRun)
                WriteFileTranscript(fullText);
            RaiseStatus(Loc.Get("Status_Ready"));
            RaiseFinished(TranscriptionOutcome.None);
            return;
        }

        long llmMs           = 0;
        long ollamaLoadMs    = 0;
        long llmPromptEvalMs = 0;
        long llmEvalMs       = 0;
        int  llmPromptTokens = 0;
        int  llmEvalTokens   = 0;
        var llmSettings = _host.Llm;
        double recDurationSec = (_recordingSw?.Elapsed.TotalSeconds) ?? 0;
        int rawWordCount = TextMetrics.CountWords(fullText);

        // File transcription and the plain transcription hotkey are verbatim.
        // Only a dedicated rewrite hotkey supplies _manualProfileName and can
        // replace the raw result; legacy auto-rule settings are not consulted.
        RewriteProfile? profile = isFileRun
            ? null
            : RewriteProfileSelection.ForHotkey(llmSettings, _manualProfileName);
        if (!isFileRun
            && llmSettings.Enabled
            && !string.IsNullOrWhiteSpace(_manualProfileName)
            && profile is null)
        {
            DeckleWhispSource.Log.ManualProfileNotFound();
            DeckleWhispSource.Log.ManualProfileNotFoundDetail(_manualProfileName);
        }

        // Preserve the raw text before any rewrite replaces fullText — the
        // corpus logger (fired at the very end) captures only the raw side.
        string rawText = fullText;

        if (profile is not null)
        {
            RaiseStatus(Loc.Format("Status_Rewriting_Format", profile.Name));
            // Narrative only after the call settles — on success we know it
            // landed, on failure we say so explicitly. The previous pre-call
            // "is now rewriting" line lied on failure by implying completion
            // (no counter-narrative was ever emitted). HUD state + polling
            // heartbeat already cover live feedback during the wait.
            var swLlm = System.Diagnostics.Stopwatch.StartNew();
            var llmResult = _rewrite.Rewrite(fullText, llmSettings.OllamaEndpoint, profile);
            swLlm.Stop();
            // Wall-clock total (caller-side) is the authoritative number for
            // user-perceived latency — includes HTTP transit + JSON parse on
            // top of the server-side Ollama timings. The structured Ollama
            // metrics flow through to the LatencyPayload so we can later
            // compare wall vs server side and isolate transit overhead.
            llmMs           = swLlm.ElapsedMilliseconds;
            ollamaLoadMs    = llmResult.OllamaLoadMs;
            llmPromptEvalMs = llmResult.PromptEvalMs;
            llmEvalMs       = llmResult.EvalMs;
            llmPromptTokens = llmResult.PromptTokens;
            llmEvalTokens   = llmResult.EvalTokens;
            if (!string.IsNullOrWhiteSpace(llmResult.Text))
            {
                fullText = llmResult.Text;
                // If the post-rewrite copy fails, the raw transcript from the
                // first copy is still on the clipboard — degrade silently to
                // the raw text instead of making a loud noise about a failure
                // that doesn't hurt the user.
                CopyToClipboard(fullText);
            }
        }

        long pasteMs = 0;
        bool pasteVerified = false;
        if (_shouldPaste)
        {
            // Synchronous rendezvous: the handler (App) hides the HUD and only
            // returns once SW_HIDE is effective on the UI thread. After this
            // point, nothing in Deckle touches activation until the end of
            // Transcribe — Ctrl+V delivery is protected.
            OnReadyToPaste?.Invoke();
            DeckleWhispSource.Log.PasteHidSync();
            var swPaste = System.Diagnostics.Stopwatch.StartNew();
            pasteVerified = PasteFromClipboard();
            swPaste.Stop();
            pasteMs = swPaste.ElapsedMilliseconds;
        }

        // Split recap into two Info lines (timings / outputs) that land under
        // Activity, plus the existing Narrative for the user-facing closing line.
        // The monolithic 200-char Verbose is gone — each line reads cleanly
        // in LogWindow and stays grep-friendly through the standard `k=v` format.
        // Outcome : Pasted on a verified paste delivery, ClipboardOnly when
        // the text made it to the clipboard but paste was disabled or refused
        // (target lost, Deckle itself, SendInput partial) — the HUD uses
        // this to flash "Copied" or the Ctrl+V reminder before hiding.
        // On a file run the paste block above never ran (_shouldPaste is forced
        // false at start), so delivery is the disk write here: success →
        // SavedToFile, a write failure degrades to ClipboardOnly (the text is
        // already on the clipboard, so the loop still closes) plus a warning.
        TranscriptionOutcome outcome;
        if (isFileRun)
        {
            outcome = WriteFileTranscript(fullText);
        }
        else
        {
            outcome = (_shouldPaste && pasteVerified) ? TranscriptionOutcome.Pasted
                                                      : TranscriptionOutcome.ClipboardOnly;
        }
        int finalWordCount = TextMetrics.CountWords(fullText);

        // Snapshot stage timers once for both the log line and the telemetry
        // payload. Each can be null/zero when the run skipped that stage —
        // coerce so the payload stays well-formed.
        //
        // Timing sourcing after the IAsrBackend split:
        //   • prod.InitMs              ← TranscriptionResult phase timing,
        //                                carried on PipelineProduction
        //   • whisperMs (pure decode)  ← total - init (clamped to 0)
        long hotkeyToCaptureMs = _hotkeySw?.ElapsedMilliseconds ?? 0;
        long recordDrainMs     = (long)_recordDrainDuration.TotalMilliseconds;
        long stopToPipelineMs  = _stopToPipelineSw?.ElapsedMilliseconds ?? 0;
        long whisperInitMs     = prod.InitMs;
        long whisperMs         = System.Math.Max(0, transcribeMsTotal - whisperInitMs);
        // Backend name is the closest stable analogue to the old
        // _strategyLabel for the telemetry surface.
        string strategyLabel = _backend.Name;

        if (isFileRun)
        {
            if (outcome == TranscriptionOutcome.SavedToFile)
                DeckleWhispSource.Log.FileTranscriptionCompleted();
            else
                DeckleWhispSource.Log.FileTranscriptionCopied();
        }
        else if (outcome == TranscriptionOutcome.Pasted)
        {
            DeckleWhispSource.Log.DictationPasted();
        }
        else
        {
            DeckleWhispSource.Log.DictationCopied();
        }
        DeckleWhispSource.Log.PipelineCompletedDetail(outcome.ToString());
        DeckleWhispSource.Log.PipelineTimings(
            recDurationSec, _modelLoadMs, hotkeyToCaptureMs, recordDrainMs,
            stopToPipelineMs, whisperInitMs,
            whisperMs, llmMs, swClip.ElapsedMilliseconds, pasteMs);
        DeckleWhispSource.Log.PipelineLlmMetrics(
            ollamaLoadMs, llmPromptEvalMs, llmEvalMs, llmPromptTokens, llmEvalTokens);
        DeckleWhispSource.Log.PipelineOutputs(
            nSeg, fullText.Length, finalWordCount, strategyLabel,
            profile?.Name ?? "(none)", outcome.ToString());

        _recordingSw?.Stop();

        // LatencyRecorded is the canonical latency.jsonl heartbeat, analysed as a
        // dictation dataset: hotkey→capture, record-drain and stop-to-pipeline are
        // its load-bearing columns, and a file run has none of those phases (all
        // zero). Emitting a file run here would seed the dataset with rows that
        // aren't dictation and skew every per-phase average, so it is skipped. The
        // human PipelineTimings/Outputs lines above still land in the LogWindow, so
        // a file run stays fully observable — it just doesn't pollute the metric.
        if (!isFileRun)
        {
            DeckleWhispSource.Log.LatencyRecorded(
                transcription_id:     _transcriptionId,
                audio_sec:            audioSec,
                model_load_ms:        _modelLoadMs,
                hotkey_to_capture_ms: hotkeyToCaptureMs,
                record_drain_ms:      recordDrainMs,
                stop_to_pipeline_ms:  stopToPipelineMs,
                whisper_init_ms:      whisperInitMs,
                whisper_ms:           whisperMs,
                llm_ms:               llmMs,
                ollama_load_ms:       ollamaLoadMs,
                llm_prompt_eval_ms:   llmPromptEvalMs,
                llm_eval_ms:          llmEvalMs,
                llm_prompt_tokens:    llmPromptTokens,
                llm_eval_tokens:      llmEvalTokens,
                clipboard_ms:         swClip.ElapsedMilliseconds,
                paste_ms:             pasteMs,
                strategy:             strategyLabel,
                n_segments:           nSeg,
                text_chars:           fullText.Length,
                text_words:           finalWordCount,
                profile:              profile?.Name ?? "",
                pasted:               pasteVerified,
                outcome:              outcome.ToString());
        }

        // Normalized corpus: two distinct events joined by
        // _transcriptionId: CorpusAsrRecorded always captures ASR output,
        // CorpusRewriteRecorded is only emitted if a rewrite profile ran. The
        // flat WAV audio under audio/<id>.wav is shared between both sides
        // through audioFileName.
        // File runs are excluded from the voice corpus: the corpus is a dataset of
        // the user's own captured dictation, and an arbitrary imported
        // file is not that — it would pollute the training distribution.
        var telemetrySettings = _host.Telemetry;
        if (telemetrySettings.CorpusEnabled && !isFileRun)
        {
            var asrSettings = _host.Transcription.Engine;

            // ASR bucket: `raw` today (Whisper, and later Voxtral in universal
            // word-for-word mode). The future named-instruction Voxtral mode
            // will take a distinct `voxtral-<instruction>` bucket when the
            // Voxtral backend is wired.
            string asrTier   = CorpusTier.Resolve(rawWordCount);
            string asrBucket = "raw";

            // Audio deduplicated per transcription. Empty when the user has
            // not enabled RecordAudioCorpus; the JSONL line remains useful
            // without a WAV.
            //
            // Which buffer lands in the WAV follows the normalized corpus
            // contract. MatchTranscription stores what the backend actually
            // received (backendAudio: processed when DSP ran, raw otherwise);
            // AlwaysRaw forces the untouched capture to keep a re-derivable
            // baseline.
            ReadOnlyMemory<float> corpusAudioMemory =
                telemetrySettings.AudioCorpusContent == AudioCorpusContent.AlwaysRaw
                    ? audio
                    : backendAudio;
            // What the WAV actually holds, for the JSONL reader: "processed" only
            // when MatchTranscription kept the DSP output — i.e. corpusAudio is a
            // buffer distinct from raw. AlwaysRaw, or the DSP off (backendAudio ==
            // audio), both leave corpusAudio referencing the untouched capture.
            string corpusContent = !corpusAudioMemory.Equals(audio) ? "processed" : "raw";
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
                bucket:                asrBucket,
                tier:                  asrTier,
                backend:               _backend.Name,
                model:                 asrSettings.Model,
                language:              asrSettings.Language,
                prompt_or_instruction: asrSettings.InitialPrompt ?? "",
                text:                  rawText,
                text_words:            rawWordCount,
                text_chars:            rawText.Length,
                duration_seconds:      recDurationSec,
                words_per_second:      recDurationSec > 0 ? rawWordCount / recDurationSec : 0,
                elapsed_ms:            whisperMs,
                audio_content:         corpusContent);

            if (profile is not null)
            {
                int rewriteWordCount = TextMetrics.CountWords(fullText);
                // Slugify already normalizes to [a-z0-9-]+; Sanitize adds
                // belt-and-suspenders protection against problematic
                // components that could slip into Id (the suffix is not
                // re-slugified; an Id leaving the factory is expected to be 12
                // hex chars, but we do not assume it).
                string rewriteBucket = CorpusPaths.Sanitize(
                    $"rewrite-{CorpusPaths.Slugify(profile.Name)}-{profile.Id}");

                DeckleWhispSource.Log.CorpusRewriteRecorded(
                    transcription_id:      _transcriptionId,
                    audio_file:            audioFileName,
                    bucket:                rewriteBucket,
                    rewrite_profile_id:    profile.Id,
                    rewrite_profile_name:  profile.Name,
                    ollama_endpoint:       llmSettings.OllamaEndpoint,
                    ollama_model:          profile.Model ?? "",
                    prompt_template_hash:  PromptTemplateHash.Of(profile),
                    text:                  fullText,
                    text_words:            rewriteWordCount,
                    text_chars:            fullText.Length,
                    elapsed_ms:            llmMs);
            }
        }

        RaiseFinished(outcome);
    }

    // ── File-transcription delivery ─────────────────────────────────────────────
    //
    // Writes the transcript to a .txt named after the source audio file, under the
    // user's configured output folder (empty = Desktop, resolved here). Called only
    // on a file run — normally after a successful clipboard copy (a write failure
    // then degrades to ClipboardOnly rather than losing the result), and best-effort
    // when the copy itself failed, so the disk keeps the text either way. The catch
    // covers the filesystem exceptions plus the invalid-path family — an empty
    // resolved directory (a profile whose Desktop is not materialized) surfaces as
    // ArgumentException from Directory.CreateDirectory and must degrade gracefully,
    // not masquerade as a pipeline crash. Anything else is a genuine bug and
    // propagates to the worker's crash handler.
    private TranscriptionOutcome WriteFileTranscript(string fullText)
    {
        string dir = TranscriptionSettingsService.ResolveFileTranscriptionOutputDirectory(
            _host.Transcription.FileTranscriptionOutputDirectory);
        string audioPath = _fileTranscriptionPath ?? "";

        try
        {
            string written = TranscriptFileWriter.Write(fullText, audioPath, dir);
            DeckleWhispSource.Log.FileTranscriptionSaved();
            DeckleWhispSource.Log.FileTranscriptionSavedDetail(written, fullText.Length);
            return TranscriptionOutcome.SavedToFile;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException
               or ArgumentException or NotSupportedException)
        {
            DeckleWhispSource.Log.FileTranscriptionWriteFailed();
            DeckleWhispSource.Log.FileTranscriptionWriteFailedDetail(ex.GetType().Name, ex.Message);
            EmitUserFeedback(FB_WARN,
                Loc.Get("FileTranscription_WriteFailed_Title"),
                Loc.Get("FileTranscription_WriteFailed_Body"),
                FB_OVERLAY);
            return TranscriptionOutcome.ClipboardOnly;
        }
    }

    // ── Presse-papier ─────────────────────────────────────────────────────────

    // Returns true on a successful copy + verified read-back. False on any of
    // the three fatal branches (GlobalAlloc, OpenClipboard, SetClipboardData) —
    // each surfaces a Critical UserFeedback. Verify-length mismatch only emits
    // a Warning since the bytes reached the clipboard; the length check is a
    // safety net against clipboard-format mangling by a third-party watcher.
    private bool CopyToClipboard(string text)
    {
        // The Win32 write + read-back verification now lives in
        // Deckle.Core.Win32Clipboard, shared with the LogWindow Copy
        // command. This method keeps the engine's observability surface: it
        // maps the structured result back onto the same EventSource events and
        // UserFeedback the inline implementation emitted, in the same order.
        ClipboardWriteResult r = Win32Clipboard.TryCopyText(text);

        DeckleWhispSource.Log.ClipboardGlobalAlloc(r.ByteCount, r.Handle);

        if (r.Status == ClipboardWriteStatus.AllocFailed)
        {
            DeckleWhispSource.Log.ClipboardAllocFailed();
            DeckleWhispSource.Log.ClipboardAllocFailedDetail(r.ByteCount);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ClipboardCopyFailed_Memory_Title"),
                Loc.Get("Engine_ClipboardCopyFailed_Memory_Body"),
                FB_REPLACEMENT);
            return false;
        }

        if (r.Status == ClipboardWriteStatus.OpenFailed)
        {
            DeckleWhispSource.Log.ClipboardOpen(false);
            DeckleWhispSource.Log.ClipboardOpenFailed();
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ClipboardUnavailable_Title"),
                Loc.Get("Engine_ClipboardUnavailable_Body"),
                FB_REPLACEMENT);
            return false;
        }

        // The clipboard opened successfully for every remaining branch.
        DeckleWhispSource.Log.ClipboardOpen(true);

        if (r.Status == ClipboardWriteStatus.SetDataFailed)
        {
            DeckleWhispSource.Log.ClipboardSetDataFailed();
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ClipboardCopyFailed_Refused_Title"),
                Loc.Get("Engine_ClipboardCopyFailed_Refused_Body"),
                FB_REPLACEMENT);
            return false;
        }

        // Bytes reached the OS clipboard. The two Verify states are advisory
        // Warnings — the read-back flagged a discrepancy but the copy landed.
        if (r.Status == ClipboardWriteStatus.VerifyMissing)
        {
            DeckleWhispSource.Log.ClipboardVerifyMissing();
            EmitUserFeedback(FB_WARN,
                Loc.Get("Engine_ClipboardIncomplete_Unverified_Title"),
                Loc.Get("Engine_ClipboardIncomplete_Unverified_Body"),
                FB_OVERLAY);
        }
        else if (r.Status == ClipboardWriteStatus.VerifyLengthMismatch)
        {
            DeckleWhispSource.Log.ClipboardVerifyMismatch();
            DeckleWhispSource.Log.ClipboardVerifyMismatchDetail(r.ExpectedChars, r.ActualChars);
            EmitUserFeedback(FB_WARN,
                Loc.Get("Engine_ClipboardIncomplete_LengthMismatch_Title"),
                Loc.Get("Engine_ClipboardIncomplete_LengthMismatch_Body"),
                FB_OVERLAY);
        }

        DeckleWhispSource.Log.ClipboardCopied();
        DeckleWhispSource.Log.ClipboardCopyComplete(r.ExpectedChars, r.ByteCount);
        return true;
    }

    // Sends Ctrl+V to whatever window currently has the foreground at Stop
    // time — but only when UI Automation confirms the focused element is a
    // text-accepting control (Edit or Document). No Start-time capture, no
    // bring-to-front, no focus comparison: the user had all the time of the
    // recording + transcription to place their cursor where they want.
    //
    // Doctrine: clipboard is the safe default. Paste only when we are confident
    // the target expects text. When in doubt — UIA refuses to answer, unknown
    // control type, foreground is Deckle itself — the text stays on the
    // clipboard and the HUD shows the Ctrl+V reminder.
    private bool PasteFromClipboard()
    {
        const uint   INPUT_KEYBOARD  = 1;
        const uint   KEYEVENTF_KEYUP = 0x0002;
        const ushort VK_CONTROL      = 0x11;
        const ushort VK_V            = 0x56;

        IntPtr fg = NativeMethods.GetForegroundWindow();
        DeckleWhispSource.Log.PasteForeground(Win32Util.DescribeHwnd(fg));

        if (fg == IntPtr.Zero)
        {
            DeckleWhispSource.Log.PasteSkippedNoForeground();
            return false;
        }

        // Refuse if the foreground is a Deckle window itself (LogWindow, HUD,
        // Settings). Avoids the false positive where we would paste into our
        // own logs while the user reads them.
        NativeMethods.GetWindowThreadProcessId(fg, out uint fgPid);
        uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        if (fgPid == ownPid)
        {
            DeckleWhispSource.Log.PasteSkippedSelfTarget();
            return false;
        }

        // UI Automation probe on the currently focused element. If the probe
        // is anything other than "yes, it's an Edit or Document", we bail out
        // to the clipboard-only path. No speculative paste.
        bool editable = UIAutomation.IsFocusedElementTextEditable(out string uiaDiag);
        DeckleWhispSource.Log.PasteUiaDiag(uiaDiag);
        if (!editable)
        {
            DeckleWhispSource.Log.PasteSkippedNotTextField();
            return false;
        }

        int cbSize = Marshal.SizeOf<INPUT>();

        var inputs = new INPUT[]
        {
            new INPUT { type = INPUT_KEYBOARD, ki_wVk = VK_CONTROL },
            new INPUT { type = INPUT_KEYBOARD, ki_wVk = VK_V },
            new INPUT { type = INPUT_KEYBOARD, ki_wVk = VK_V,       ki_dwFlags = KEYEVENTF_KEYUP },
            new INPUT { type = INPUT_KEYBOARD, ki_wVk = VK_CONTROL, ki_dwFlags = KEYEVENTF_KEYUP },
        };

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, cbSize);
        if (sent != inputs.Length)
        {
            DeckleWhispSource.Log.PasteSendInputPartial();
            DeckleWhispSource.Log.PasteSendInputPartialDetail((int)sent, inputs.Length);
            return false;
        }

        DeckleWhispSource.Log.PasteSucceeded();
        DeckleWhispSource.Log.PasteSent(Win32Util.DescribeHwnd(fg));
        return true;
    }
}
