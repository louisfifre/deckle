using System;
using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Transcription.Whisper;

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

        public NativeAllocations(IntPtr lang, IntPtr prompt, IntPtr regex)
        {
            Language = lang;
            InitialPrompt = prompt;
            SuppressRegex = regex;
        }

        public void Free()
        {
            if (Language != IntPtr.Zero) Marshal.FreeCoTaskMem(Language);
            if (InitialPrompt != IntPtr.Zero) Marshal.FreeCoTaskMem(InitialPrompt);
            if (SuppressRegex != IntPtr.Zero) Marshal.FreeCoTaskMem(SuppressRegex);
        }
    }

    // Applies user parameters to the native struct. The struct is passed by
    // ref: overwrite the relevant fields, leave the others (strategy,
    // callbacks, n_threads...) as initialized by whisper.cpp defaults.
    //
    // `modelsDirectory` is currently unused: it fed the built-in Silero VAD model
    // lookup, now unplugged (see the VAD section below). Kept on the signature for
    // when the built-in path is revisited.
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
        string prompt = promptOverride ?? whisp.Engine.InitialPrompt;
        IntPtr langPtr = Marshal.StringToCoTaskMemUTF8(whisp.Engine.Language);
        IntPtr promptPtr = Marshal.StringToCoTaskMemUTF8(prompt);
        wparams.language = langPtr;
        wparams.initial_prompt = promptPtr;
        wparams.carry_initial_prompt =
            (byte)(whisp.Engine.CarryInitialPrompt && prompt.Length > 0 ? 1 : 0);

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
        // Whisper's built-in Silero VAD stays unplugged: the external VAD
        // (Deckle.Vad, Streaming.SpeechTrim) owns chunk cleaning on the consumer
        // side, so re-running whisper's VAD per utterance would be redundant and
        // slow. Forcing vad = 0 is the unplug.
        wparams.vad = 0;

        return new NativeAllocations(langPtr, promptPtr, regexPtr);
    }
}
