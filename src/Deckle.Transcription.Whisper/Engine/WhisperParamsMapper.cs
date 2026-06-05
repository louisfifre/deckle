using System;
using System.IO;
using System.Runtime.InteropServices;
using Deckle.Core.Interop;
using Deckle.Transcription;
using Deckle.Transcription.Whisper.Pinvoke;
using Deckle.Transcription.Whisper.Setup;

namespace Deckle.Transcription.Whisper.Engine;

// ── WhisperParamsMapper ───────────────────────────────────────────────────────
//
// Bridge between TranscriptionSettings (user-oriented model) and
// WhisperFullParams (native C struct). Called by TranscriptionEngine.Transcribe()
// just before whisper_full(), after the struct has been initialized through
// whisper_full_default_params_by_ref().
//
// ONLY touches hot-reload fields: anything that does not require relaunching the
// whisper_init context. Model choice and use_gpu are applied separately at
// LoadModelAsync() time (see TranscriptionEngine._modelPath).
//
// Returned unmanaged allocations: the caller is responsible for freeing them
// after whisper_full() through FreeAllocations().
public static class WhisperParamsMapper
{
    public readonly struct NativeAllocations
    {
        public readonly IntPtr Language;
        public readonly IntPtr InitialPrompt;
        public readonly IntPtr SuppressRegex;
        public readonly IntPtr VadModelPath;

        public NativeAllocations(IntPtr lang, IntPtr prompt, IntPtr regex, IntPtr vadPath)
        {
            Language = lang;
            InitialPrompt = prompt;
            SuppressRegex = regex;
            VadModelPath = vadPath;
        }

        public void Free()
        {
            if (Language != IntPtr.Zero) Marshal.FreeCoTaskMem(Language);
            if (InitialPrompt != IntPtr.Zero) Marshal.FreeCoTaskMem(InitialPrompt);
            if (SuppressRegex != IntPtr.Zero) Marshal.FreeCoTaskMem(SuppressRegex);
            if (VadModelPath != IntPtr.Zero) Marshal.FreeCoTaskMem(VadModelPath);
        }
    }

    // Applies user parameters to the native struct. The struct is passed by
    // ref: overwrite the relevant fields, leave the others (strategy,
    // callbacks, n_threads...) as initialized by whisper.cpp defaults.
    //
    // `modelsDirectory` is the folder where the Silero VAD model is looked up:
    // resolved host-side (ITranscriptionEngineHost.ResolveModelsDirectory) so
    // this module stays independent from the app SettingsService.
    //
    // `promptOverride` (optional) replaces the configured initial_prompt for
    // THIS call: this is the channel through which the streaming base injects
    // its inter-utterance context (fixed prompt + previous utterance tail).
    // Null = keep the stylistic prompt from settings (unchanged monolithic
    // path).
    public static NativeAllocations Apply(
        ref WhisperFullParams wparams,
        TranscriptionSettings whisp,
        string modelsDirectory,
        string? promptOverride = null)
    {
        // ── Transcription ─────────────────────────────────────────────────
        IntPtr langPtr = Marshal.StringToCoTaskMemUTF8(whisp.Engine.Language);
        IntPtr promptPtr = Marshal.StringToCoTaskMemUTF8(promptOverride ?? whisp.Engine.InitialPrompt);
        wparams.language = langPtr;
        wparams.initial_prompt = promptPtr;
        wparams.carry_initial_prompt = (byte)(whisp.Engine.CarryInitialPrompt ? 1 : 0);

        // ── Confidence Thresholds ─────────────────────────────────────────
        wparams.entropy_thold = (float)whisp.Confidence.EntropyThreshold;
        wparams.logprob_thold = (float)whisp.Confidence.LogprobThreshold;
        wparams.no_speech_thold = (float)whisp.Confidence.NoSpeechThreshold;

        // ── Decoding ──────────────────────────────────────────────────────
        wparams.temperature = (float)whisp.Decoding.Temperature;
        wparams.temperature_inc = (float)whisp.Decoding.TemperatureIncrement;

        // Beam search: strategy 1 = WHISPER_SAMPLING_BEAM_SEARCH.
        // Explores multiple decoding paths and keeps the best overall
        // sequence. Better quality than greedy (strategy 0), slower.
        if (whisp.Decoding.UseBeamSearch)
        {
            wparams.strategy = 1;
            wparams.beam_search_beam_size = whisp.Decoding.BeamSize;
        }

        // ── Output Filters ────────────────────────────────────────────────
        wparams.suppress_blank = (byte)(whisp.OutputFilters.SuppressBlank ? 1 : 0);
        wparams.suppress_nst = (byte)(whisp.OutputFilters.SuppressNonSpeechTokens ? 1 : 0);

        IntPtr regexPtr = IntPtr.Zero;
        if (!string.IsNullOrEmpty(whisp.OutputFilters.SuppressRegex))
        {
            regexPtr = Marshal.StringToCoTaskMemUTF8(whisp.OutputFilters.SuppressRegex);
            wparams.suppress_regex = regexPtr;
        }

        // ── Context and Segmentation ──────────────────────────────────────
        // UseContext (UI) = inverse of no_context (native).
        wparams.no_context = (byte)(whisp.Context.UseContext ? 0 : 1);
        // MaxTokens <= 0 means "auto" — leave whisper.cpp's default (16384).
        // Writing -1 here makes whisper.cpp compute
        // max_prompt_ctx = min(-1, n_text_ctx/2) = -1 then clamp the initial
        // prompt to 1 token, surfacing a confusing "initial prompt is too long"
        // warning on every transcription.
        if (whisp.Context.MaxTokens > 0)
            wparams.n_max_text_ctx = whisp.Context.MaxTokens;

        // ── VAD ───────────────────────────────────────────────────────────
        IntPtr vadPathPtr = IntPtr.Zero;
        wparams.vad = (byte)(whisp.SpeechDetection.Enabled ? 1 : 0);

        if (whisp.SpeechDetection.Enabled)
        {
            // Silero model looked up in the models folder supplied by the
            // host. If absent, VAD disabled with log warning; no native crash.
            // VAD filename + download URL sourced from the
            // Setup catalog so the engine and the wizard agree on which
            // Silero version to ship.
            string vadModelPath = Path.Combine(
                modelsDirectory,
                SpeechModels.VadModelFileName);

            if (File.Exists(vadModelPath))
            {
                vadPathPtr = Marshal.StringToCoTaskMemUTF8(vadModelPath);
                wparams.vad_model_path = vadPathPtr;
                wparams.vad_threshold = whisp.SpeechDetection.Threshold;
                wparams.vad_min_speech_duration_ms = whisp.SpeechDetection.MinSpeechDurationMs;
                wparams.vad_min_silence_duration_ms = whisp.SpeechDetection.MinSilenceDurationMs;
                wparams.vad_max_speech_duration_s = whisp.SpeechDetection.MaxSpeechDurationSec;
                wparams.vad_speech_pad_ms = whisp.SpeechDetection.SpeechPadMs;
                wparams.vad_samples_overlap = whisp.SpeechDetection.SamplesOverlap;
            }
            else
            {
                wparams.vad = 0;
                DeckleWhispSource.Log.WhisperLogWarning(
                    $"Silero VAD model not found at {vadModelPath} — VAD disabled. " +
                    $"Download from {SpeechModels.VadModel.Url}");
            }
        }

        return new NativeAllocations(langPtr, promptPtr, regexPtr, vadPathPtr);
    }
}
