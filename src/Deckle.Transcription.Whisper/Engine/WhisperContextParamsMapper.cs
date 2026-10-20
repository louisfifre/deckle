using Deckle.Transcription;

namespace Deckle.Transcription.Whisper;

// Maps TranscriptionSettings onto WhisperContextParams just before
// whisper_init_from_file_with_params(), after
// whisper_context_default_params_by_ref() has initialized the struct.
//
// Load-time counterpart of WhisperParamsMapper: these fields bind the context
// for its whole life, so a change reaches whisper only at the next model load.
public static class WhisperContextParamsMapper
{
    // Writes only the user-tunable fields; whisper's defaults keep the rest
    // (gpu_device, flash_attn, DTW).
    public static void Apply(ref WhisperContextParams ctxParams, TranscriptionSettings whisp)
    {
        // use_gpu = 0 makes whisper skip its GPU device search and build the
        // context on the CPU backend alone.
        ctxParams.use_gpu = (byte)(whisp.Engine.UseGpu ? 1 : 0);
    }
}
