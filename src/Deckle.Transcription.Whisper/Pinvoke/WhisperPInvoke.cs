using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Transcription.Whisper;

// ── WhisperPInvoke ──────────────────────────────────────────────────────────
//
// Whisper.cpp P/Invokes plus a managed DllImportResolver that loads
// libwhisper.dll from <UserDataRoot>\native\ rather than from the application
// binary directory. Both pieces are Whisp-domain — they leave Deckle.Core
// unburdened of Whisper-specific knowledge while keeping the resolver wired
// to THIS assembly so the [DllImport("libwhisper")] attributes below resolve
// correctly.
//
// libwhisper.dll and its transitive ggml-*.dll dependencies live under
// <UserDataRoot>\native\, NOT alongside the application binary. The
// initializer wires a managed resolver that loads libwhisper from that
// directory; Windows then resolves the ggml-*.dll dependencies from the same
// directory automatically (DLL load order: directory of the loaded DLL first).
//
// Runs on first access to any WhisperPInvoke member, before any
// [DllImport(PInvokeKey)] P/Invoke is executed — guaranteed by the CLR's
// static-constructor contract.
//
// The PInvokeKey constant below MUST stay in sync with the literal in every
// [DllImport("libwhisper")] attribute. C# requires a constant literal in the
// attribute, so the duplication is unavoidable — keep PInvokeKey as the
// documented match-target.
//
// Fails closed when NativeDirectory doesn't hold libwhisper.dll: the
// resolver throws DllNotFoundException rather than returning IntPtr.Zero.
// Returning Zero would hand control back to the CLR's default search
// order, which probes the application base directory — silently widening
// the load surface to a per-user-writable install dir (DLL-planting
// exposure). The first-run wizard gates on NativeRuntime.IsInstalled()
// before WhisperBackend is ever constructed, so this throw is a
// defence-in-depth backstop, not an expected runtime path: by the time
// any [DllImport("libwhisper")] fires, the entry DLL is guaranteed
// present in NativeDirectory. The throw only surfaces if that invariant
// is somehow violated (the gate bypassed) — and then it names the
// expected path instead of loading an attacker-planted DLL.
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
        // Non-libwhisper names are not ours: return Zero so the CLR resolves
        // them through its normal search order. Today every [DllImport] in this
        // assembly targets "libwhisper", so this branch is dead in practice —
        // it keeps the contract correct for any future DllImport added here.
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

    // ── libwhisper.dll ────────────────────────────────────────────────────────

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

    // ── whisper_log_set: Global Callback For Internal Logs ───────────────────
    //
    // whisper.cpp continuously emits log lines (model loading, decoding start,
    // GPU metrics, timings, etc.). By default they go to stderr where they are
    // lost. By wiring a callback, redirect all of it to the LogWindow.
    //
    // C signature: void (*)(enum ggml_log_level level, const char *text, void *user_data)
    // ggml_log_level levels: 0=None, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Cont.

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WhisperLogCallback(int level, IntPtr text, IntPtr user_data);

    [DllImport("libwhisper", CallingConvention = CallingConvention.Cdecl)]
    public static extern void whisper_log_set(WhisperLogCallback log_callback, IntPtr user_data);
}
