using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Transcription.Whisper;

// Whisper.cpp P/Invokes plus the DllImportResolver that hands whisper.dll,
// ggml.dll and ggml-base.dll to NativeRuntimeLoader, which loads them from
// <UserDataRoot>\native\ with the search limited to that folder and System32.
// The static constructor wires the resolver before any [DllImport] in this
// class runs.
//
// WhisperKey, GgmlKey and GgmlBaseKey MUST match the literal in every
// [DllImport("whisper")], [DllImport("ggml")] and [DllImport("ggml-base")]:
// C# requires a constant literal in the attribute. The registry of backends is
// split between the two ggml DLLs: ggml.dll loads and lists them, ggml-base.dll
// owns each entry (its name, its devices). A P/Invoke on the wrong one fails
// with EntryPointNotFoundException at the first model load.
//
// Startup checks that the runtime is installed and loads (NativeRuntime.CheckLoad)
// before constructing WhisperBackend, so the resolver's fail-closed throw is a
// backstop, not an expected path.
public static class WhisperPInvoke
{
    private const string WhisperKey  = "whisper";
    private const string GgmlKey     = "ggml";
    private const string GgmlBaseKey = "ggml-base";
    private const string EntryDll    = "whisper.dll";
    private const string GgmlDll     = "ggml.dll";
    private const string GgmlBaseDll = "ggml-base.dll";

    static WhisperPInvoke()
    {
        NativeLibrary.SetDllImportResolver(typeof(WhisperPInvoke).Assembly, ResolveNativeLibrary);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Other names are not ours: Zero hands them back to the CLR's normal
        // search order.
        string? dll = libraryName switch
        {
            WhisperKey  => EntryDll,
            GgmlKey     => GgmlDll,
            GgmlBaseKey => GgmlBaseDll,
            _           => null,
        };
        if (dll is null) return IntPtr.Zero;

        // Fail closed: the loader throws instead of returning Zero, which
        // would let the CLR fall back to its default search and probe the
        // application base directory, a per-user-writable location where a
        // planted DLL could be loaded. Its exception names the first DLL
        // that did not load and the import it misses.
        return NativeRuntimeLoader.LoadForPInvoke(AppPaths.NativeDirectory, dll);
    }

    // ── ggml backend registry ───────────────────────────────────────────
    //
    // The runtime is built with GGML_BACKEND_DL: ggml.dll has no backend
    // compiled in and registers nothing until asked to load them from a
    // folder. whisper.cpp never asks, so NativeRuntimeLoader does, before the
    // first model load.

    [DllImport("ggml", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ggml_backend_load_all_from_path(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dir_path);

    [DllImport("ggml", CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint ggml_backend_reg_count();

    [DllImport("ggml", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ggml_backend_reg_get(nuint index);

    // Static string owned by the backend ("CPU", "Vulkan"): read with
    // PtrToStringUTF8, never freed. Exported by ggml-base.dll, not ggml.dll.
    [DllImport("ggml-base", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ggml_backend_reg_name(IntPtr reg);

    [DllImport("ggml", CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint ggml_backend_dev_count();

    // ── whisper.cpp ─────────────────────────────────────────────────────

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_context_default_params_by_ref();

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern void whisper_free_context_params(IntPtr ptr);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_init_from_file_with_params(
        [MarshalAs(UnmanagedType.LPStr)] string path_model,
        WhisperContextParams cparams);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_full_default_params_by_ref(int strategy);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern void whisper_free_params(IntPtr ptr);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_full(IntPtr ctx, WhisperFullParams wparams, float[] samples, int n_samples);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_full_n_segments(IntPtr ctx);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_full_get_segment_text(IntPtr ctx, int i_segment);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern long whisper_full_get_segment_t0(IntPtr ctx, int i_segment);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern long whisper_full_get_segment_t1(IntPtr ctx, int i_segment);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern float whisper_full_get_segment_no_speech_prob(IntPtr ctx, int i_segment);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_full_n_tokens(IntPtr ctx, int i_segment);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_full_get_token_id(IntPtr ctx, int i_segment, int i_token);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern float whisper_full_get_token_p(IntPtr ctx, int i_segment, int i_token);

    // Returns the id from which tokens are timestamps (<|0.00|>, <|5.30|>...).
    // Any token whose id is >= this value is a timestamp token, not text.
    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern int whisper_token_beg(IntPtr ctx);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern void whisper_free(IntPtr ctx);

    // Without a callback, whisper.cpp's log lines go to stderr and are lost.
    // C signature: void (*)(enum ggml_log_level level, const char *text, void *user_data)
    // ggml_log_level levels: 0=None, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Cont.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WhisperLogCallback(int level, IntPtr text, IntPtr user_data);

    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern void whisper_log_set(WhisperLogCallback log_callback, IntPtr user_data);

    // Static strings owned by libwhisper: read with PtrToStringUTF8, never
    // freed. whisper_version is absent from older builds, so a hand-imported
    // runtime can throw EntryPointNotFoundException here.
    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_version();

    // Registers ggml's backends on its first call: calling it before the first
    // context creation would start the GPU device enumeration early.
    [DllImport("whisper",CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr whisper_print_system_info();
}
