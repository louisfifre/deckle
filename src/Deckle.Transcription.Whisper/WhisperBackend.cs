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

// IAsrBackend implementation backed by whisper.cpp. Every P/Invoke detail and
// whisper.cpp idiom stays here, so the orchestrator in Deckle.Transcription
// only deals with the IAsrBackend contract.
public sealed partial class WhisperBackend : IAsrBackend
{
    public string Name => "whisper";

    private readonly ITranscriptionEngineHost _host;
    private readonly object _modelLock = new();
    // whisper.cpp is not thread-safe across concurrent calls on one context (a
    // native segfault no managed handler can rescue). The engine already keeps
    // the prime and the real transcription apart; this lock keeps the backend
    // safe without relying on its caller.
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

    private readonly WhisperNativeLogCompactor _logCompactor = new();

    // Static like the log hook that feeds it: ggml prints its device table
    // once per process, and every later load needs it to name the GPU.
    private static readonly WhisperComputeDeviceObserver s_computeDevice = new();

    // Open while consecutive loads fail: the engine retries at every take, and
    // the Warning fires once per incident.
    private bool _loadFailureOpen;

    public bool IsModelLoaded => _ctx != IntPtr.Zero;
    public string? DetectedAccelerator => _ctx == IntPtr.Zero ? null : _detectedBackend;

    public WhisperBackend(ITranscriptionEngineHost host)
    {
        _host = host;
        InstallWhisperLogHook();
        LogNativeRuntimeIdentity();
    }
}
