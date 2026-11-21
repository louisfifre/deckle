using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Deckle.App.SelfCheck;

// What the run measured about the process itself, for the measurement bench
// (scripts/bench/perf-bench.ps1). The durations of pages and windows are not
// here: the report's surfaces and the log's WindowLoadComplete events already
// carry them, and the bench reads those.
//
//   StartupReadyMs   from the process start to the end of the settings surface
//                    wiring: the self-check's "ready". It is not the resident
//                    boot (the self-check leaves most of it out, see
//                    LeftOutOfSelfCheck), so compare it with itself only.
//   Footprint        taken once the walk has opened every window, the app's warm
//                    state for this mode; a --only run has opened fewer, so it
//                    compares with runs of the same target only.
//   Dictation        set only when the bench asked for it (DECKLE_BENCH_DICTATION).
internal sealed class SelfCheckPerf
{
    public long StartupReadyMs { get; set; }
    public SelfCheckFootprint? Footprint { get; set; }
    public SelfCheckDictation? Dictation { get; set; }

    // Again after the take, with the model loaded: what the engine keeps.
    public SelfCheckFootprint? FootprintAfterDictation { get; set; }
}

internal sealed record SelfCheckFootprint(
    long WorkingSetBytes, long PrivateWorkingSetBytes, long PrivateBytes, int Handles, int Threads)
{
    public static SelfCheckFootprint Read()
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        long privateWorkingSet = 0;
        var counters = new ProcessMemoryCountersEx2 { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>() };
        if (K32GetProcessMemoryInfo(process.Handle, ref counters, counters.Size))
            privateWorkingSet = (long)counters.PrivateWorkingSetSize;
        return new SelfCheckFootprint(
            process.WorkingSet64, privateWorkingSet, process.PrivateMemorySize64, process.HandleCount, process.Threads.Count);
    }

    // PROCESS_MEMORY_COUNTERS_EX2: the one structure that carries the private
    // working set (the pages only this process holds in memory).
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    private static extern bool K32GetProcessMemoryInfo(nint process, ref ProcessMemoryCountersEx2 counters, uint size);
}

// One take of the dictation pipeline on a file, through the real engine.
//   Outcome   ok, skipped (the machine has no runtime or model: a fact, not a
//             fault) or failed (the engine returned no text).
//   WallMs    the call to TranscribeFileToTextAsync, from request to text.
//   Phases (model load, decode, ...) are the engine's own PipelineTimings line
//   in the run's log; the transcript itself is never kept, only its length.
internal sealed record SelfCheckDictation(
    string Outcome, string? Reason, string? Backend, long WallMs, int TextChars)
{
    public const string Ok = "ok";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}
