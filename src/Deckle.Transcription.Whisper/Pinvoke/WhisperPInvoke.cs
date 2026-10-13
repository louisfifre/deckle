using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Transcription.Whisper;

// Whisper.cpp P/Invokes plus the DllImportResolver that loads libwhisper.dll
// from <UserDataRoot>\native\, not from the application directory. Windows then
// resolves the ggml-*.dll dependencies from that same directory (directory of
// the loaded DLL first). The static constructor wires the resolver before any
// [DllImport] in this class runs.
//
// PInvokeKey MUST match the literal in every [DllImport("libwhisper")]: C#
// requires a constant literal in the attribute.
//
// Startup checks NativeRuntime.IsInstalled() before constructing WhisperBackend,
// so the resolver's fail-closed throw is a backstop, not an expected path.
public static class WhisperPInvoke
{
    private const string PInvokeKey = "libwhisper";
    private const string EntryDll   = "libwhisper.dll";

    static WhisperPInvoke()
    {
        NativeLibrary.SetDllImportResolver(typeof(WhisperPInvoke).Assembly, ResolveNativeLibrary);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Other names are not ours: Zero hands them back to the CLR's normal
        // search order.
        if (libraryName != PInvokeKey) return IntPtr.Zero;

        // Fail closed: only ever load libwhisper by its absolute path under
        // NativeDirectory. If the file is absent, throw instead of returning
        // Zero — returning Zero would let the CLR fall back to default search
        // order and probe the application base directory, a per-user-writable
        // location where a planted libwhisper.dll could be loaded.
        string candidate = Path.Combine(AppPaths.NativeDirectory, EntryDll);
        if (!File.Exists(candidate))
            throw new DllNotFoundException(
                $"The Whisper native runtime is not provisioned: expected '{EntryDll}' " +
                $"at '{candidate}'. Run the first-run setup to install it. " +
                $"Default DLL search is deliberately not used (DLL-planting hardening).");

        if (!NativeLibrary.TryLoad(candidate, out IntPtr handle))
            throw new DllNotFoundException(
                $"Found '{EntryDll}' at '{candidate}' but it could not be loaded " +
                $"(corrupt, locked, or an unmet transitive dependency such as a " +
                $"missing ggml-*.dll alongside it).");

        return handle;
    }

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_context_default_params_by_ref();

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern void whisper_free_context_params(IntPtr ptr);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_init_from_file_with_params(
        [MarshalAs(UnmanagedType.LPStr)] string path_model,
        WhisperContextParams cparams);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_full_default_params_by_ref(int strategy);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern void whisper_free_params(IntPtr ptr);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_full(IntPtr ctx, WhisperFullParams wparams, float[] samples, int n_samples);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_full_n_segments(IntPtr ctx);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_full_get_segment_text(IntPtr ctx, int i_segment);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern long whisper_full_get_segment_t0(IntPtr ctx, int i_segment);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern long whisper_full_get_segment_t1(IntPtr ctx, int i_segment);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern float whisper_full_get_segment_no_speech_prob(IntPtr ctx, int i_segment);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_full_n_tokens(IntPtr ctx, int i_segment);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_full_get_token_id(IntPtr ctx, int i_segment, int i_token);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern float whisper_full_get_token_p(IntPtr ctx, int i_segment, int i_token);

    // Returns the id from which tokens are timestamps (<|0.00|>, <|5.30|>...).
    // Any token whose id is >= this value is a timestamp token, not text.
    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_token_beg(IntPtr ctx);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern void whisper_free(IntPtr ctx);

    // Without a callback, whisper.cpp's log lines go to stderr and are lost.
    // C signature: void (*)(enum ggml_log_level level, const char *text, void *user_data)
    // ggml_log_level levels: 0=None, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Cont.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WhisperLogCallback(int level, IntPtr text, IntPtr user_data);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern void whisper_log_set(WhisperLogCallback log_callback, IntPtr user_data);
}
