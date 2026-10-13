using System;
using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Transcription.Whisper;

// Maps TranscriptionSettings onto WhisperFullParams just before whisper_full(),
// after whisper_full_default_params_by_ref() has initialized the struct.
//
// ONLY touches hot-reload fields: anything that does not require relaunching the
// whisper_init context. Model choice and use_gpu apply at model load.
//
// The caller frees the returned unmanaged allocations after whisper_full()
// through NativeAllocations.Free().
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

    // Writes only the user-tunable fields; the caller owns the rest
    // (callbacks, print channels, n_threads).
    //
    // `modelsDirectory` is unused: it would locate the model for whisper's
    // built-in VAD, which stays off (see the vad line below).
    //
    // `promptOverride` replaces the configured initial_prompt for this call: the
    // streaming path injects its inter-utterance context through it. Null keeps
    // the stylistic prompt from settings.
    public static NativeAllocations Apply(
        ref WhisperFullParams wparams,
        TranscriptionSettings whisp,
        string modelsDirectory,
        string? promptOverride = null)
    {
        string prompt = promptOverride ?? whisp.Engine.InitialPrompt;
        IntPtr langPtr = Marshal.StringToCoTaskMemUTF8(whisp.Engine.Language);
        IntPtr promptPtr = Marshal.StringToCoTaskMemUTF8(prompt);
        wparams.language = langPtr;
        wparams.initial_prompt = promptPtr;
        wparams.carry_initial_prompt =
            (byte)(whisp.Engine.CarryInitialPrompt && prompt.Length > 0 ? 1 : 0);

        // Decoder guards that drive the fallback and skip decisions on a decoded
        // segment, not voice-activity detection (whisper's own VAD is off below).
        wparams.entropy_thold = (float)whisp.Confidence.EntropyThreshold;
        wparams.logprob_thold = (float)whisp.Confidence.LogprobThreshold;
        wparams.no_speech_thold = (float)whisp.Confidence.NoSpeechThreshold;

        wparams.temperature = (float)whisp.Decoding.Temperature;
        wparams.temperature_inc = (float)whisp.Decoding.TemperatureIncrement;

        // strategy 1 = WHISPER_SAMPLING_BEAM_SEARCH (0 = greedy).
        if (whisp.Decoding.UseBeamSearch)
        {
            wparams.strategy = 1;
            wparams.beam_search_beam_size = whisp.Decoding.BeamSize;
        }

        wparams.suppress_blank = (byte)(whisp.OutputFilters.SuppressBlank ? 1 : 0);
        wparams.suppress_nst = (byte)(whisp.OutputFilters.SuppressNonSpeechTokens ? 1 : 0);

        IntPtr regexPtr = IntPtr.Zero;
        if (!string.IsNullOrEmpty(whisp.OutputFilters.SuppressRegex))
        {
            regexPtr = Marshal.StringToCoTaskMemUTF8(whisp.OutputFilters.SuppressRegex);
            wparams.suppress_regex = regexPtr;
        }

        wparams.no_context = (byte)(whisp.Context.UseContext ? 0 : 1);
        // MaxTokens <= 0 means "auto" — leave whisper.cpp's default (16384).
        // Writing -1 here makes whisper.cpp compute
        // max_prompt_ctx = min(-1, n_text_ctx/2) = -1 then clamp the initial
        // prompt to 1 token, surfacing a confusing "initial prompt is too long"
        // warning on every transcription.
        if (whisp.Context.MaxTokens > 0)
            wparams.n_max_text_ctx = whisp.Context.MaxTokens;

        // Whisper's built-in VAD stays off: the external VAD (Deckle.Vad,
        // Streaming.SpeechTrim) cleans the chunks upstream, so re-running it per
        // utterance would be redundant and slow.
        wparams.vad = 0;

        return new NativeAllocations(langPtr, promptPtr, regexPtr);
    }
}
