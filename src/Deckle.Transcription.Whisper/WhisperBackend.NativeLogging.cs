using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using Deckle.Core;

namespace Deckle.Transcription.Whisper;

public sealed partial class WhisperBackend
{
    // Counts the log lines whose handling threw. The first one opens a Warning
    // for the process; the rest stay in the gated detail.
    private static int s_logLineFailures;

    // What libwhisper.dll reports about itself, read once per process.
    // "unknown" until read, or for a build without whisper_version.
    private static string s_runtimeVersion = "unknown";
    private static int s_runtimeIdentityLogged;
    private static int s_systemInfoLogged;

    private void InstallWhisperLogHook()
    {
        Volatile.Write(ref s_logOwner, new WeakReference<WhisperBackend>(this));

        if (Volatile.Read(ref s_logHookInstalled) != 0) return;

        lock (s_logHookLock)
        {
            if (s_logHookInstalled != 0) return;

            try
            {
                WhisperPInvoke.whisper_log_set(s_logCallback, IntPtr.Zero);
                Volatile.Write(ref s_logHookInstalled, 1);
            }
            catch (Exception ex)
            {
                DeckleWhisperSource.Log.LogHookUnavailable(ex.GetType().Name, ex.Message);
            }
        }
    }

    // Runs right after the log hook, so a runtime that loaded but has no hook
    // still names itself. Once per process: the DLL cannot change underneath.
    private static void LogNativeRuntimeIdentity()
    {
        if (Interlocked.Exchange(ref s_runtimeIdentityLogged, 1) != 0) return;

        try
        {
            s_runtimeVersion = Marshal.PtrToStringUTF8(WhisperPInvoke.whisper_version()) ?? "unknown";
        }
        catch (EntryPointNotFoundException ex)
        {
            // A build older than the whisper_version export: the file details
            // below still identify it.
            DeckleWhisperSource.Log.NativeRuntimeProbeFailed("version", ex.GetType().Name, ex.Message);
        }
        catch (Exception ex)
        {
            // The DLL itself did not load: LogHookUnavailable already carries
            // that cause as a Warning. The next backend tries again.
            DeckleWhisperSource.Log.NativeRuntimeProbeFailed("version", ex.GetType().Name, ex.Message);
            Volatile.Write(ref s_runtimeIdentityLogged, 0);
            return;
        }

        DeckleWhisperSource.Log.NativeRuntimeLoaded(s_runtimeVersion, NativeRuntime.CurrentBundle.Version);

        string path = Path.Combine(AppPaths.NativeDirectory, NativeRuntime.EntryDll);
        try
        {
            var file = new FileInfo(path);
            string fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "";
            DeckleWhisperSource.Log.NativeRuntimeLoadedDetail(
                path, file.Length, fileVersion, file.LastWriteTimeUtc.ToString("O"));
        }
        catch (Exception ex)
        {
            DeckleWhisperSource.Log.NativeRuntimeProbeFailed("file_details", ex.GetType().Name, ex.Message);
        }
    }

    // After the first successful init only: whisper_print_system_info loads
    // ggml's backends, which the context creation has done by then.
    private static void LogNativeSystemInfoOnce()
    {
        if (Interlocked.Exchange(ref s_systemInfoLogged, 1) != 0) return;
        try
        {
            string features = Marshal.PtrToStringUTF8(WhisperPInvoke.whisper_print_system_info())?.Trim() ?? "";
            DeckleWhisperSource.Log.NativeRuntimeSystemInfo(features);
        }
        catch (Exception ex)
        {
            DeckleWhisperSource.Log.NativeRuntimeProbeFailed("system_info", ex.GetType().Name, ex.Message);
        }
    }

    private static void DispatchWhisperLog(int level, IntPtr textPtr, IntPtr userData)
    {
        try
        {
            WeakReference<WhisperBackend>? ownerReference = Volatile.Read(ref s_logOwner);
            if (ownerReference is null || !ownerReference.TryGetTarget(out WhisperBackend? owner)) return;

            owner.HandleWhisperLog(level, textPtr);
        }
        catch (Exception ex)
        {
            // Never let an exception cross the native boundary, not even one
            // thrown while reporting this one.
            try
            {
                int failures = Interlocked.Increment(ref s_logLineFailures);
                if (failures == 1)
                    DeckleWhisperSource.Log.LogHookLineFailed(ex.GetType().Name, ex.Message);
                DeckleWhisperSource.Log.LogHookLineFailedDetail(failures, ex.GetType().Name, ex.Message);
            }
            catch
            {
                // Nothing left to report through.
            }
        }
    }

    private void HandleWhisperLog(int level, IntPtr textPtr)
    {
        string msg = Marshal.PtrToStringUTF8(textPtr)?.TrimEnd('\r', '\n', ' ') ?? "";
        if (string.IsNullOrEmpty(msg)) return;

        // The observer turns a GPU init failure into one Warning per incident
        // at the end of the load, so the native ERROR line stays Verbose here.
        bool reportedByObserver = s_computeDevice.Observe(msg);

        // ggml levels: 0=None, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Cont.
        // whisper.cpp sends its routine lines at Info, so only Warn and Error
        // rise above Verbose. They pass before the compactor: folded into a
        // Verbose phase summary, a native warning would vanish from the
        // default log.
        if (level is 3 or 4 && !reportedByObserver)
        {
            _logCompactor.FlushPendingPhase();
            if (level == 4) DeckleWhisperSource.Log.NativeLogError(msg);
            else DeckleWhisperSource.Log.NativeLogWarning(msg);
            return;
        }

        if (_logCompactor.TryAccumulatePhaseLine(msg)) return;

        // "no GPU found" lands here at Info: the VAD context creation reports
        // it on every take (whisper.cpp hardcodes use_gpu=false there), and a
        // model load that really lacks the GPU is reported by the observer.
        DeckleWhisperSource.Log.NativeLogLine(level, msg);
    }
}
