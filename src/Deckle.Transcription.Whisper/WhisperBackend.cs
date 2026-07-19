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

// ── WhisperBackend ───────────────────────────────────────────────────────────
//
// IAsrBackend implementation backed by whisper.cpp through the WhisperPInvoke
// surface. Encapsulates every P/Invoke detail and every whisper.cpp idiom
// (native log callback, segment callback) so the orchestrator in
// Deckle.Transcription only deals with the IAsrBackend contract.
//
// Threading. LoadModelAsync runs synchronously inside Task.Run-friendly code
// (whisper_init is blocking); the orchestrator calls it from a background
// worker. TranscribeAsync wraps the synchronous whisper_full behind an async
// signature — future backends with real HTTP/IPC plumbing will fill the
// signature with actual asynchrony. UnloadModel can fire from a timer at
// any time; the model lock prevents it from racing an in-flight Transcribe.
//
// Lifetime. The static whisper_log_set callback is process-wide; we install
// it once at construction and never reset it (whisper.cpp keeps the function
// pointer indefinitely; clearing it would have to coordinate with any other
// libwhisper consumer in the process, which today does not exist). The
// segment + abort callbacks are per-call, kept rooted in instance fields
// for the duration of whisper_full.
public sealed partial class WhisperBackend : IAsrBackend
{
    public string Name => "whisper";

    private readonly ITranscriptionEngineHost _host;
    private readonly object _modelLock = new();
    // Serialises whisper_full calls on the same _ctx. The orchestrator keeps the
    // prime's dummy inference and the real transcription from overlapping at a
    // higher level — the prime now runs on its own thread, concurrently with the
    // capture, and the engine gates the first real call behind it (AwaitPrime) —
    // so no concurrent caller is expected in practice. The lock is the hard
    // backend-local guard underneath that: whisper.cpp is not thread-safe across
    // concurrent calls on a single context (a native segfault no managed handler
    // can rescue), and the IAsrBackend contract must not assume its caller stays
    // serialised forever.
    private readonly object _transcribeLock = new();

    // volatile: prevents the JIT from caching this in a register so a
    // background thread sees the real handle, not a stale snapshot.
    private volatile IntPtr _ctx = IntPtr.Zero;
    private volatile string _detectedBackend = "CPU";
    private bool _disposed;

    // whisper_log_set is process-global and retains the function pointer for
    // the process lifetime. Root one static thunk permanently, install it once,
    // and route it weakly to the latest live backend instance.
    private static readonly object s_logHookLock = new();
    private static readonly WhisperPInvoke.WhisperLogCallback s_logCallback = DispatchWhisperLog;
    private static WeakReference<WhisperBackend>? s_logOwner;
    private static int s_logHookInstalled;

    // Per-call callbacks only need to stay rooted for whisper_full.
    private WhisperNewSegmentCallback? _segmentCallback;
    private WhisperAbortCallback? _abortCallback;

    // Init-phase log compactor — owns the per-phase string state machine that
    // consolidates whisper.cpp's noisy init lines into one event per phase.
    private readonly WhisperNativeLogCompactor _logCompactor = new();

    // ── IAsrBackend surface ──────────────────────────────────────────────────

    public bool IsModelLoaded => _ctx != IntPtr.Zero;
    public string? DetectedAccelerator => _ctx == IntPtr.Zero ? null : _detectedBackend;

    public WhisperBackend(ITranscriptionEngineHost host)
    {
        _host = host;
        InstallWhisperLogHook();
    }
}
