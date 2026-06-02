using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Audio.Preprocessing;
using Deckle.Audio.Telemetry;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Core.Interop;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription.Corpus;
using Deckle.Transcription.Engine;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── Pipeline partial — Transcribe + clipboard + paste + post-record helpers ────────────────────────────────────────

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


    // Auto-calibration heuristic — runs after every Recording when
    // LevelWindow.AutoCalibrationEnabled is true, independent of the
    // Log microphone toggle (the payload is always computed in
    // LogRecordingTelemetry above).
    //
    // Strategy:
    //   - Keep the last N MicrophoneTelemetryPayloads in a ring buffer
    //     (N = LevelWindow.AutoCalibrationSamples, default 5).
    //   - Once the buffer is full, recompute MinDbfs / MaxDbfs from
    //     median-across-sessions percentiles, with margins:
    //       MinDbfs = median(p25) - 5 dB  — p25 (not p10) so a noise gate
    //                                       cutting to digital silence
    //                                       (-97 dBFS) doesn't drag the
    //                                       floor into "anything below
    //                                       the gate threshold". Then
    //                                       -5 dB of headroom under the
    //                                       useful-signal minimum.
    //       MaxDbfs = median(p90) + 5 dB  — voice ceiling with breathing
    //                                       room above routine peaks.
    //   - Floor clamp at -75 dBFS to guarantee we never sit on the gate
    //     even if p25 itself is in the noise floor.
    //   - Refuse to write if the resulting window collapses to < 10 dB
    //     (pathological case — e.g. all-silence sessions).
    //   - Push to settings + AudioLevelMapper + log a Success line.
    //
    // The buffer is in-memory only: a fresh app launch starts collecting
    // again, which is fine — calibration only fires after N consecutive
    // recordings within one process anyway, and the persisted Min/Max
    // already reflects the last successful auto-calibration.
    //
    // The user's manual slider edits override auto-calibration until the
    // next time it fires — there's no "manual flag" gating; whoever wrote
    // last wins, which is the natural behaviour from the user's POV.
    private void TryAutoCalibrate(MicrophoneTelemetryPayload payload)
    {
        var lw = _host.Audio.LevelWindow;
        if (!lw.AutoCalibrationEnabled) return;

        int needed = Math.Max(1, lw.AutoCalibrationSamples);

        _autoCalibBuffer.Enqueue(payload);
        while (_autoCalibBuffer.Count > needed) _autoCalibBuffer.Dequeue();
        if (_autoCalibBuffer.Count < needed) return;

        // Pure compute lives in MicrophoneCalibrationCalculator — the
        // constants (-5 dB / +5 dB margins, -75 floor, ≥10 dB spread,
        // [-90,-10] / [-60,-10] clamps, 0.5 dB no-change tolerance) are
        // preserved exactly. The enveloppe (ring buffer, SaveSettings,
        // ApplyLevelWindow, log) stays here because the side effects
        // belong to the orchestrator.
        var calib = MicrophoneCalibrationCalculator.Compute(
            _autoCalibBuffer, lw.MinDbfs, lw.MaxDbfs);
        if (!calib.ShouldUpdate) return;

        lw.MinDbfs = calib.NewMinDbfs;
        lw.MaxDbfs = calib.NewMaxDbfs;
        _host.SaveSettings();

        // Push live into HudChrono so the next sub-window already uses the
        // new calibration. The host owns the static-field write
        // (App.ApplyLevelWindow on the App side).
        _host.ApplyLevelWindow(lw);

        DeckleWhispSource.Log.AutoCalibrated(calib.NewMinDbfs, calib.NewMaxDbfs, needed);
    }


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
    // the strategy runs (corpus join key, ADR-0006).
    private void FinalizeTranscription(PipelineProduction prod)
    {
        string  fullText          = prod.RawText;
        float[] audio             = prod.RawAudio;
        float[] backendAudio      = prod.BackendAudio;
        float   audioSec          = (float)audio.Length / 16_000f;
        long    transcribeMsTotal = prod.TotalTranscribeMs;
        int     nSeg              = prod.NSegments;

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

        // Rewrite profile resolution:
        // - manual rewrite hotkey → the profile name passed to StartRecording
        // - plain transcribe hotkey → first matching AutoRewriteRule (duration-based)
        RewriteProfile? profile = null;
        if (!string.IsNullOrWhiteSpace(_manualProfileName) && llmSettings.Enabled)
        {
            profile = llmSettings.Profiles.Find(p =>
                string.Equals(p.Name, _manualProfileName, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                DeckleWhispSource.Log.ManualProfileNotFound(_manualProfileName);
            }
        }
        else if (llmSettings.Enabled)
        {
            // Pivot between the two auto-rule lists. "Words" is the default —
            // word count is a truer proxy for LLM context load than wall-clock
            // duration. "Duration" keeps the legacy behaviour.
            RewriteProfile? ResolveRuleProfile(string? id, string? name)
            {
                var byId = !string.IsNullOrEmpty(id)
                    ? llmSettings.Profiles.Find(p => p.Id == id)
                    : null;
                return byId ?? llmSettings.Profiles.Find(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            bool byWords = !string.Equals(llmSettings.RuleMetric, "Duration", StringComparison.OrdinalIgnoreCase);
            if (byWords && llmSettings.AutoRewriteRulesByWords.Count > 0)
            {
                foreach (var rule in llmSettings.AutoRewriteRulesByWords
                    .OrderByDescending(r => r.MinWordCount))
                {
                    if (rawWordCount >= rule.MinWordCount)
                    {
                        profile = ResolveRuleProfile(rule.ProfileId, rule.ProfileName);
                        break;
                    }
                }
            }
            else if (!byWords && llmSettings.AutoRewriteRules.Count > 0)
            {
                foreach (var rule in llmSettings.AutoRewriteRules
                    .OrderByDescending(r => r.MinDurationSeconds))
                {
                    if (recDurationSec >= rule.MinDurationSeconds)
                    {
                        profile = ResolveRuleProfile(rule.ProfileId, rule.ProfileName);
                        break;
                    }
                }
            }
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
            var llmResult = _llm.Rewrite(fullText, llmSettings.OllamaEndpoint, profile);
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
        var outcome = (_shouldPaste && pasteVerified) ? TranscriptionOutcome.Pasted
                                                      : TranscriptionOutcome.ClipboardOnly;
        int finalWordCount = TextMetrics.CountWords(fullText);

        // Snapshot stage timers once for both the log line and the telemetry
        // payload. Each can be null/zero when the run skipped that stage —
        // coerce so the payload stays well-formed.
        //
        // Timing sourcing after the IAsrBackend split:
        //   • prod.InitMs, prod.VadMs  ← TranscriptionResult phase timings,
        //                                carried on PipelineProduction
        //   • whisperMs (pure decode)  ← total - init - vad (clamped to 0)
        //   • vadInferenceMs (Silero
        //     CPU time, distinct from
        //     wall-clock vad)          ← no longer surfaced after the split,
        //                                kept in the payload as 0 until a
        //                                backend exposes it through the
        //                                interface.
        long hotkeyToCaptureMs = _hotkeySw?.ElapsedMilliseconds ?? 0;
        long recordDrainMs     = (long)_recordDrainDuration.TotalMilliseconds;
        long stopToPipelineMs  = _stopToPipelineSw?.ElapsedMilliseconds ?? 0;
        long whisperInitMs     = prod.InitMs;
        long vadMs             = prod.VadMs;
        long whisperMs         = System.Math.Max(0, transcribeMsTotal - whisperInitMs - vadMs);
        long vadInferenceMs    = 0;
        // Backend name is the closest stable analogue to the old
        // _strategyLabel for the telemetry surface.
        string strategyLabel = _backend.Name;

        DeckleWhispSource.Log.PipelineCompleted(outcome.ToString());
        DeckleWhispSource.Log.PipelineTimings(
            recDurationSec, _modelLoadMs, hotkeyToCaptureMs, recordDrainMs,
            stopToPipelineMs, whisperInitMs, vadMs, vadInferenceMs,
            whisperMs, llmMs, swClip.ElapsedMilliseconds, pasteMs);
        DeckleWhispSource.Log.PipelineLlmMetrics(
            ollamaLoadMs, llmPromptEvalMs, llmEvalMs, llmPromptTokens, llmEvalTokens);
        DeckleWhispSource.Log.PipelineOutputs(
            nSeg, fullText.Length, finalWordCount, strategyLabel,
            profile?.Name ?? "(none)", outcome.ToString());

        RaiseStatus(Loc.Get("Status_Ready"));
        _recordingSw?.Stop();

        DeckleWhispSource.Log.LatencyRecorded(
            audio_sec:            audioSec,
            model_load_ms:        _modelLoadMs,
            hotkey_to_capture_ms: hotkeyToCaptureMs,
            record_drain_ms:      recordDrainMs,
            stop_to_pipeline_ms:  stopToPipelineMs,
            whisper_init_ms:      whisperInitMs,
            vad_ms:               vadMs,
            vad_inference_ms:     vadInferenceMs,
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

        // Corpus normalisé — voir ADR-0006. Deux events distincts joints
        // par _transcriptionId : CorpusAsrRecorded capte toujours la
        // sortie ASR, CorpusRewriteRecorded n'est émis que si un profil
        // rewrite a tourné. L'audio WAV plat sous audio/<id>.wav est
        // partagé entre les deux côtés via audioFileName.
        var telemetrySettings = _host.Telemetry;
        if (telemetrySettings.CorpusEnabled)
        {
            var asrSettings = _host.Transcription.Engine;

            // Bucket ASR : `raw` aujourd'hui (Whisper, et plus tard
            // Voxtral en mode mot-pour-mot universel). Le futur mode
            // Voxtral instruction-nommée prendra un bucket
            // `voxtral-<instruction>` distinct quand le backend Voxtral
            // sera branché.
            string asrTier   = CorpusTier.Resolve(rawWordCount);
            string asrBucket = "raw";

            // Audio dédupliqué par transcription. Vide quand l'utilisateur
            // n'a pas activé RecordAudioCorpus — la ligne JSONL reste
            // utile sans WAV.
            //
            // Quel buffer atterrit dans le WAV — choix utilisateur (ADR-0006,
            // amendement 2026-06-02). MatchTranscription stocke ce que le
            // backend a réellement reçu (backendAudio : traité quand le DSP a
            // tourné, brut sinon) ; AlwaysRaw force la capture intouchée pour
            // garder une baseline ré-dérivable.
            float[] corpusAudio =
                telemetrySettings.AudioCorpusContent == AudioCorpusContent.AlwaysRaw
                    ? audio
                    : backendAudio;
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
                elapsed_ms:            whisperMs);

            if (profile is not null)
            {
                int rewriteWordCount = TextMetrics.CountWords(fullText);
                // Slugify normalise déjà en [a-z0-9-]+ ; Sanitize ajoute
                // une ceinture-bretelles contre les composants problématiques
                // qui pourraient se glisser dans Id (le suffixe n'est pas
                // re-slugifié — un Id sortant de la fabrique est censé
                // être 12 hex chars mais on ne le présume pas).
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

    // ── Presse-papier ─────────────────────────────────────────────────────────

    // Returns true on a successful copy + verified read-back. False on any of
    // the three fatal branches (GlobalAlloc, OpenClipboard, SetClipboardData) —
    // each surfaces a Critical UserFeedback. Verify-length mismatch only emits
    // a Warning since the bytes reached the clipboard; the length check is a
    // safety net against clipboard-format mangling by a third-party watcher.
    private bool CopyToClipboard(string text)
    {
        // The Win32 write + read-back verification now lives in
        // Deckle.Core.Interop.Win32Clipboard, shared with the LogWindow Copy
        // command. This method keeps the engine's observability surface: it
        // maps the structured result back onto the same EventSource events and
        // UserFeedback the inline implementation emitted, in the same order.
        ClipboardWriteResult r = Win32Clipboard.TryCopyText(text);

        DeckleWhispSource.Log.ClipboardGlobalAlloc(r.ByteCount, r.Handle);

        if (r.Status == ClipboardWriteStatus.AllocFailed)
        {
            DeckleWhispSource.Log.ClipboardAllocFailed(r.ByteCount);
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
            DeckleWhispSource.Log.ClipboardVerifyMismatch(r.ExpectedChars, r.ActualChars);
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
            DeckleWhispSource.Log.PasteSendInputPartial((int)sent, inputs.Length);
            return false;
        }

        DeckleWhispSource.Log.PasteSucceeded();
        DeckleWhispSource.Log.PasteSent(Win32Util.DescribeHwnd(fg));
        return true;
    }
}
