using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using Deckle.Core;

namespace Deckle.Transcription.Whisper;

// The one place Deckle hands whisper.cpp files to the Windows loader. It
// loads them the way the tester's machine will: each DLL by its full path,
// with the search limited to the native folder and System32. Never PATH, never
// the working directory, never the application folder. On the build machine
// PATH holds the compiler's own runtime, which once hid a missing DLL until the
// first outside tester; excluding PATH makes the build machine fail the same
// way, before a release.
//
// Three callers: the P/Invoke resolver asks for the whisper.dll and ggml.dll
// handles, the setup wizard asks whether the installed catalog loads here
// (Check), and the model load asks ggml to load its backends from the same
// folder (EnsureBackendsLoaded). Everything specific to loading the runtime
// lives in this file: take it out and whisper.cpp goes back to the CLR's
// default search.
internal static class NativeRuntimeLoader
{
    // LoadLibraryExW flags: the directory of the DLL being loaded, applied to
    // its whole import chain, plus System32. No PATH, no current directory.
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchSystem32   = 0x00000800;
    private const uint BundleSearch = LoadLibrarySearchDllLoadDir | LoadLibrarySearchSystem32;

    public const int ErrorModNotFound    = 126;  // an import could not be found
    public const int ErrorBadExeFormat   = 193;  // not a 64-bit Windows DLL
    public const int ErrorDllInitFailed  = 1114; // DllMain returned FALSE

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    // Handles of the catalog DLLs loaded for P/Invoke, kept for the process:
    // whisper.cpp keeps threads and registries inside them.
    private static readonly object s_loadLock = new();
    private static readonly Dictionary<string, IntPtr> s_loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly OnceOnSuccess s_backends = new();

    // Loads the catalog up to and including dllName, in catalog order, and
    // returns dllName's handle. The backends are skipped: ggml loads them.
    // Throws DllNotFoundException naming the first DLL that does not load and
    // the import it misses, after logging the same facts.
    public static IntPtr LoadForPInvoke(string nativeDirectory, string dllName)
    {
        lock (s_loadLock)
        {
            if (s_loaded.TryGetValue(dllName, out IntPtr cached)) return cached;

            foreach (string name in NativeRuntime.RequiredDllNames)
            {
                if (NativeRuntime.IsBackendDll(name)) continue;
                if (s_loaded.ContainsKey(name)) continue;

                NativeDllLoadResult result = TryLoad(nativeDirectory, name, out IntPtr handle);
                if (!result.Loaded)
                {
                    DeckleWhisperSource.Log.NativeRuntimeLoadFailed(
                        result.Name, result.MissingImport ?? "", result.Win32Error, result.Summary);
                    throw new DllNotFoundException(
                        $"The whisper.cpp runtime could not be loaded: {result.Summary} " +
                        "Run the first-run setup again to repair the native runtime.");
                }
                s_loaded[name] = handle;
                if (string.Equals(name, dllName, StringComparison.OrdinalIgnoreCase)) return handle;
            }

            throw new DllNotFoundException($"'{dllName}' is not in the whisper.cpp runtime catalog.");
        }
    }

    // Loads every catalog DLL once, in order, and frees them again: the
    // answer is whether they load here, not a loaded runtime. A DLL already
    // loaded for P/Invoke is not freed by this (Windows counts references).
    public static NativeRuntimeLoadCheck Check(string nativeDirectory)
    {
        var results = new List<NativeDllLoadResult>(NativeRuntime.RequiredDllNames.Count);
        var handles = new List<IntPtr>();
        try
        {
            foreach (string name in NativeRuntime.RequiredDllNames)
            {
                NativeDllLoadResult result = TryLoad(nativeDirectory, name, out IntPtr handle);
                results.Add(result);
                if (result.Loaded) handles.Add(handle);
            }
        }
        finally
        {
            for (int i = handles.Count - 1; i >= 0; i--) FreeLibrary(handles[i]);
        }

        bool coreLoads = true;
        bool anyCpuBackend = false;
        bool vulkan = false;
        foreach (NativeDllLoadResult r in results)
        {
            if (NativeRuntime.IsVulkanBackend(r.Name)) vulkan = r.Loaded;
            else if (NativeRuntime.IsBackendDll(r.Name)) anyCpuBackend |= r.Loaded;
            else coreLoads &= r.Loaded;
        }

        var check = new NativeRuntimeLoadCheck(coreLoads && anyCpuBackend, vulkan, results);
        if (check.Loads)
        {
            DeckleWhisperSource.Log.NativeRuntimeLoadCheckPassed(vulkan);
        }
        else
        {
            NativeDllLoadResult? failure = check.FirstFailure;
            DeckleWhisperSource.Log.NativeRuntimeLoadCheckFailed(
                failure?.Name ?? "", failure?.MissingImport ?? "", failure?.Win32Error ?? 0,
                failure?.Summary ?? "no CPU backend loaded");
        }
        return check;
    }

    // Asks ggml to load its backends from the native folder, once per
    // process, and says which ones answered. ggml searches that folder for
    // ggml-<backend>-*.dll, loads each with a plain LoadLibraryW and keeps
    // the best-scoring variant per backend; the C++ runtime those files
    // import is already in the process by then, brought in by whisper.dll.
    //
    // Once means once it worked: a call that throws (ggml.dll missing an
    // export, the folder half-written) or registers nothing leaves the next
    // model load free to try again, after a repair, in the same process. A
    // run that registered backends is never repeated, since ggml would
    // register them a second time.
    public static void EnsureBackendsLoaded(string nativeDirectory) =>
        s_backends.Run(() => LoadBackends(nativeDirectory));

    private static bool LoadBackends(string nativeDirectory)
    {
        // ggml loads every ggml-*.dll of the folder, so an older bundle's
        // ggml-cpu.dll (MinGW) beside the MSVC catalog lands in the process
        // and aborts it. Provisioning already purges, but a folder filled
        // before that purge existed, or by hand, reaches here unpurged. The
        // stale files are not loaded yet: LoadForPInvoke loads catalog DLLs only.
        NativeRuntime.RemoveStale(nativeDirectory);
        WhisperPInvoke.ggml_backend_load_all_from_path(nativeDirectory);

        nuint count = WhisperPInvoke.ggml_backend_reg_count();
        var names = new List<string>((int)count);
        bool vulkan = false;
        for (nuint i = 0; i < count; i++)
        {
            string name = Marshal.PtrToStringUTF8(WhisperPInvoke.ggml_backend_reg_name(WhisperPInvoke.ggml_backend_reg_get(i))) ?? "?";
            names.Add(name);
            vulkan |= name.Equals("Vulkan", StringComparison.OrdinalIgnoreCase);
        }
        string joined = string.Join(",", names);
        long devices = (long)WhisperPInvoke.ggml_backend_dev_count();

        if (count == 0)
        {
            DeckleWhisperSource.Log.NativeBackendsNone(nativeDirectory);
            return false;
        }
        DeckleWhisperSource.Log.NativeBackendsLoaded(joined, devices);
        if (!vulkan) DeckleWhisperSource.Log.NativeBackendVulkanUnavailable(joined);
        return true;
    }

    private static NativeDllLoadResult TryLoad(string nativeDirectory, string name, out IntPtr handle)
    {
        string path = Path.Combine(nativeDirectory, name);
        if (!File.Exists(path))
        {
            handle = IntPtr.Zero;
            return new NativeDllLoadResult(name, false, 0, null, $"{name} is not in the native folder.");
        }

        handle = LoadLibraryExW(path, IntPtr.Zero, BundleSearch);
        if (handle != IntPtr.Zero)
            return new NativeDllLoadResult(name, true, 0, null, $"{name} loads.");

        int error = Marshal.GetLastWin32Error();
        string? missing = error == ErrorModNotFound ? FindMissingImport(nativeDirectory, path) : null;
        string summary = error switch
        {
            ErrorModNotFound when missing is not null =>
                $"{name} imports {missing}, which is neither beside it in the native folder nor in System32 (Win32 error 126).",
            ErrorModNotFound =>
                $"{name} needs an import that could not be found (Win32 error 126).",
            ErrorBadExeFormat =>
                $"{name} is not a 64-bit Windows DLL (Win32 error 193).",
            ErrorDllInitFailed =>
                $"{name} failed its own initialization (Win32 error 1114).",
            _ =>
                $"{name} could not be loaded: {Marshal.GetPInvokeErrorMessage(error).Trim()} (Win32 error {error}).",
        };
        return new NativeDllLoadResult(name, false, error, missing, summary);
    }

    // Which import of a DLL that failed with "module not found" is absent:
    // the first one that is neither beside it nor loadable from System32
    // alone. An import that is in the catalog failed on its own line already,
    // so it is named too when its file is absent.
    private static string? FindMissingImport(string nativeDirectory, string path)
    {
        IReadOnlyList<string> imports;
        // A diagnosis must never throw over the failure it explains: an
        // unreadable or malformed image (PeImports turns a truncated one into
        // BadImageFormatException too) leaves the import unnamed, and the
        // summary falls back to "needs an import that could not be found".
        try { imports = PeImports.ReadImportedDllNames(path); }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException
                                      or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }

        foreach (string import in imports)
        {
            if (File.Exists(Path.Combine(nativeDirectory, import))) continue;
            IntPtr probe = LoadLibraryExW(import, IntPtr.Zero, LoadLibrarySearchSystem32);
            if (probe != IntPtr.Zero) { FreeLibrary(probe); continue; }
            return import;
        }
        return null;
    }
}
