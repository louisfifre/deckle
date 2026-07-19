using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Transcription.Whisper;

public sealed partial class WhisperBackend
{
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
                DeckleWhispSource.Log.WhisperLogSetUnavailable();
                DeckleWhispSource.Log.WhisperLogSetUnavailableDetail(ex.Message);
            }
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
        catch
        {
            // Never let an exception cross the native boundary.
        }
    }

    private void HandleWhisperLog(int level, IntPtr textPtr)
    {
        string msg = Marshal.PtrToStringUTF8(textPtr)?.TrimEnd('\r', '\n', ' ') ?? "";
        if (string.IsNullOrEmpty(msg)) return;

        // ── Backend detection (first hit wins, sticks) ───────────
        if (_detectedBackend == "CPU")
        {
            if (msg.StartsWith("ggml_vulkan:", StringComparison.Ordinal))
                _detectedBackend = "Vulkan";
            else if (msg.StartsWith("ggml_cuda_init:", StringComparison.Ordinal) ||
                     msg.StartsWith("ggml_cuda:", StringComparison.Ordinal))
                _detectedBackend = "CUDA";
            else if (msg.StartsWith("ggml_metal_init:", StringComparison.Ordinal) ||
                     msg.StartsWith("ggml_metal:", StringComparison.Ordinal))
                _detectedBackend = "Metal";
        }

        // ── Init-phase compaction ────────────────────────────────
        // Each phase prefix accumulates its own values; the moment a
        // different prefix is seen, the accumulated phase is flushed
        // as a single event before the new phase starts. Lines that
        // are not from a tracked phase flush any pending phase first,
        // then fall through to the level switch below.
        if (_logCompactor.TryAccumulatePhaseLine(msg)) return;

        // ── "no GPU found" downgrade for the second backend init ─
        // The VAD context creation triggers a second whisper_backend
        // _init_gpu that always reports "no GPU found" (whisper.cpp
        // hardcodes use_gpu=false in whisper_vad_init_context). Benign
        // but alarming at Warn — keep it Verbose. Targeted match so a
        // real GPU failure phrased differently still surfaces.
        if (msg.StartsWith("whisper_backend_init_gpu", StringComparison.Ordinal) &&
            msg.IndexOf("no GPU found", StringComparison.Ordinal) >= 0)
        {
            DeckleWhispSource.Log.WhisperLogVerbose(msg);
            return;
        }

        // ggml levels: 0=None, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Cont.
        switch (level)
        {
            case 4:
                DeckleWhispSource.Log.WhisperLogError();
                DeckleWhispSource.Log.WhisperLogErrorDetail(msg);
                break;
            case 3:
                DeckleWhispSource.Log.WhisperLogWarning();
                DeckleWhispSource.Log.WhisperLogWarningDetail(msg);
                break;
            default: DeckleWhispSource.Log.WhisperLogVerbose(msg); break;
        }
    }
}
