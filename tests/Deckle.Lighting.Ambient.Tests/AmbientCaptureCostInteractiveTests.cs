using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Deckle.Vision;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// Measures what one capture rate costs on this machine, to decide whether the
// capture-rate setting (15/30/60 Hz) could give way to a cap decided in code.
// It drives the real ScreenCaptureService and FrameSampler, the pair the
// engine runs, plus AmbientZoneSampler over the sampled grid, at several
// target rates, and records frames delivered, the capture thread's CPU time
// per frame and the sampler's wall time per frame.
//
// It needs a physical display and an interactive desktop, so it is explicit
// and never part of an ordinary test run:
//   artifacts\bin\Deckle.Lighting.Ambient.Tests\debug\Deckle.Lighting.Ambient.Tests.exe
//     -class Deckle.Lighting.Ambient.Tests.AmbientCaptureCostInteractiveTests -explicit only
// Close the resident Deckle first if Ambient runs, and run nothing else heavy:
// the numbers are only as clean as the machine.
//
// A static desktop gives Output Duplication nothing to deliver, so a 32×32
// window in the bottom-right corner changes its opacity at every composition
// (DwmFlush), which makes the desktop update at the display's refresh rate.
// Deckle's own per-frame work does not depend on what changed: the sampler
// copies and reduces the whole frame either way.
[Trait("Category", "interactive")]
public sealed class AmbientCaptureCostInteractiveTests
{
    private static readonly int[] TargetRatesHz = [60, 120, 240, 500];
    private static readonly TimeSpan Warmup = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Measure = TimeSpan.FromSeconds(10);

    [Fact(Explicit = true)]
    public void MeasureCaptureCostPerRate()
    {
        // DuplicateOutput1 answers DXGI_ERROR_UNSUPPORTED to a process that is
        // not per-monitor DPI aware (v2). Deckle declares it in its manifest;
        // the test host has none, so it opts in before touching DXGI.
        Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        var report = new StringBuilder();
        var monitors = ScreenCaptureService.GetAvailableMonitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        report.AppendLine(CultureInfo.InvariantCulture,
            $"# Capture cost, {DateTime.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Display {primary.Width}x{primary.Height}, {Environment.ProcessorCount} logical CPUs.");
        report.AppendLine();

        using var animator = DesktopAnimator.Start();

        var baseline = MeasureProcessOnly();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Baseline (animator only): process CPU {baseline.ProcessCpuMsPerS:F1} ms/s, animator {animator.FlipsPerSecond():F0} flips/s.");
        report.AppendLine();
        report.AppendLine("| target Hz | delivered fps | capture thread CPU ms/frame | process CPU ms/s over baseline | sampler wall ms/frame mean / p50 / p95 | 4 zones µs/frame | format | grid | readback B/frame |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|");

        foreach (int rate in TargetRatesHz)
        {
            var r = MeasureRate(rate);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {rate} | {r.DeliveredFps:F1} | {r.CaptureThreadCpuMsPerFrame:F3} | {r.ProcessCpuMsPerS - baseline.ProcessCpuMsPerS:F1} | " +
                $"{r.SamplerMeanMs:F3} / {r.SamplerP50Ms:F3} / {r.SamplerP95Ms:F3} | {r.ZonesUsPerFrame:F2} | {r.Format} | {r.Grid} | {r.ReadbackBytes} |");
        }

        string text = report.ToString();
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        string dir = Path.Combine(FindRepoRoot(), "artifacts", "capture-cost");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"capture-cost-{DateTime.Now:yyyyMMdd-HHmmss}.md"), text);
    }

    private sealed record RateResult(
        double DeliveredFps,
        double CaptureThreadCpuMsPerFrame,
        double ProcessCpuMsPerS,
        double SamplerMeanMs,
        double SamplerP50Ms,
        double SamplerP95Ms,
        double ZonesUsPerFrame,
        string Format,
        string Grid,
        int ReadbackBytes);

    private sealed record Baseline(double ProcessCpuMsPerS);

    private static Baseline MeasureProcessOnly()
    {
        Thread.Sleep(Warmup);
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpu0 = process.TotalProcessorTime;
        var sw = Stopwatch.StartNew();
        Thread.Sleep(Measure);
        process.Refresh();
        double seconds = sw.Elapsed.TotalSeconds;
        return new Baseline((process.TotalProcessorTime - cpu0).TotalMilliseconds / seconds);
    }

    private static RateResult MeasureRate(int targetHz)
    {
        using var capture = new ScreenCaptureService(targetHz);
        capture.Start();
        var sampler = new FrameSampler(
            capture.Device!, capture.ContentSize, capture.ActiveFormat, capture.PeakLuminance);
        try
        {
            int bandRows = AmbientZoneSampler.ResolveBandCells(BorderThicknessMode.Share, 0.15, 0, sampler.GridRows);
            int bandCols = AmbientZoneSampler.ResolveBandCells(BorderThicknessMode.Share, 0.15, 0, sampler.GridCols);

            var samplerTicks = new List<long>(8192);
            long zoneTicks = 0;
            long frames = 0;
            bool measuring = false;
            nint captureThread = 0;
            float sink = 0;

            capture.FrameArrived += frame =>
            {
                if (captureThread == 0)
                    captureThread = Native.OpenThread(Native.THREAD_QUERY_LIMITED_INFORMATION, false, Native.GetCurrentThreadId());

                long t0 = Stopwatch.GetTimestamp();
                bool ok = sampler.Process(frame);
                long t1 = Stopwatch.GetTimestamp();
                if (!ok || !Volatile.Read(ref measuring)) return;

                var sample = sampler.LatestSample!;
                long z0 = Stopwatch.GetTimestamp();
                sink += AmbientZoneSampler.SampleZone(sample, LightZone.Top, bandRows).R;
                sink += AmbientZoneSampler.SampleZone(sample, LightZone.Bottom, bandRows).R;
                sink += AmbientZoneSampler.SampleZone(sample, LightZone.Left, bandCols).R;
                sink += AmbientZoneSampler.SampleZone(sample, LightZone.Right, bandCols).R;
                zoneTicks += Stopwatch.GetTimestamp() - z0;

                samplerTicks.Add(t1 - t0);
                frames++;
            };

            Thread.Sleep(Warmup);
            Assert.NotEqual(0, captureThread);

            var process = Process.GetCurrentProcess();
            process.Refresh();
            var processCpu0 = process.TotalProcessorTime;
            long threadCpu0 = Native.ThreadCpu100ns(captureThread);
            var sw = Stopwatch.StartNew();
            Volatile.Write(ref measuring, true);
            Thread.Sleep(Measure);
            Volatile.Write(ref measuring, false);
            double seconds = sw.Elapsed.TotalSeconds;
            long threadCpu1 = Native.ThreadCpu100ns(captureThread);
            process.Refresh();
            var processCpu1 = process.TotalProcessorTime;

            capture.Stop();
            Native.CloseHandle(captureThread);
            GC.KeepAlive(sink);

            long[] sorted = samplerTicks.ToArray();
            Array.Sort(sorted);
            double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
            int bytesPerPixel = sampler.IsHdr ? 8 : 4;

            return new RateResult(
                DeliveredFps: frames / seconds,
                CaptureThreadCpuMsPerFrame: frames == 0 ? 0 : (threadCpu1 - threadCpu0) / 10_000.0 / frames,
                ProcessCpuMsPerS: (processCpu1 - processCpu0).TotalMilliseconds / seconds,
                SamplerMeanMs: sorted.Length == 0 ? 0 : ToMs((long)sorted.Average()),
                SamplerP50Ms: sorted.Length == 0 ? 0 : ToMs(sorted[sorted.Length / 2]),
                SamplerP95Ms: sorted.Length == 0 ? 0 : ToMs(sorted[(int)(sorted.Length * 0.95)]),
                ZonesUsPerFrame: frames == 0 ? 0 : ToMs(zoneTicks) * 1000.0 / frames,
                Format: sampler.IsHdr ? "FP16" : "BGRA8",
                Grid: $"{sampler.GridCols}x{sampler.GridRows}",
                ReadbackBytes: sampler.GridCols * sampler.GridRows * bytesPerPixel);
        }
        finally
        {
            sampler.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Deckle.Tests.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    // A 32×32 topmost, non-activating window whose opacity flips at every
    // DWM composition, on its own thread with its own message pump.
    private sealed class DesktopAnimator : IDisposable
    {
        private readonly Thread _thread;
        private readonly CancellationTokenSource _cts = new();
        private long _flips;

        private DesktopAnimator()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "capture-cost-animator" };
        }

        public static DesktopAnimator Start()
        {
            var animator = new DesktopAnimator();
            animator._thread.Start();
            return animator;
        }

        // Counts the flips over one second, so the report shows the
        // composition rate the capture had to follow.
        public double FlipsPerSecond()
        {
            long before = Interlocked.Read(ref _flips);
            Thread.Sleep(TimeSpan.FromSeconds(1));
            return Interlocked.Read(ref _flips) - before;
        }

        private void Run()
        {
            int sx = Native.GetSystemMetrics(Native.SM_CXSCREEN);
            int sy = Native.GetSystemMetrics(Native.SM_CYSCREEN);
            nint hwnd = Native.CreateWindowExW(
                Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
                "STATIC", "capture-cost", Native.WS_POPUP | Native.WS_VISIBLE,
                sx - 32, sy - 32, 32, 32, 0, 0, 0, 0);
            if (hwnd == 0) return;

            byte alpha = 255;
            while (!_cts.IsCancellationRequested)
            {
                while (Native.PeekMessageW(out var msg, 0, 0, 0, Native.PM_REMOVE))
                {
                    Native.TranslateMessage(in msg);
                    Native.DispatchMessageW(in msg);
                }
                alpha = alpha == 255 ? (byte)128 : (byte)255;
                Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA);
                Native.DwmFlush();
                Interlocked.Increment(ref _flips);
            }
            Native.DestroyWindow(hwnd);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _thread.Join(TimeSpan.FromSeconds(2));
            _cts.Dispose();
        }
    }

    private static class Native
    {
        public const uint THREAD_QUERY_LIMITED_INFORMATION = 0x0800;
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;
        public const uint WS_POPUP = 0x80000000;
        public const uint WS_VISIBLE = 0x10000000;
        public const uint WS_EX_LAYERED = 0x00080000;
        public const uint WS_EX_TOPMOST = 0x00000008;
        public const uint WS_EX_TOOLWINDOW = 0x00000080;
        public const uint WS_EX_NOACTIVATE = 0x08000000;
        public const uint LWA_ALPHA = 0x2;
        public const uint PM_REMOVE = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public nint hwnd;
            public uint message;
            public nint wParam;
            public nint lParam;
            public uint time;
            public int ptX;
            public int ptY;
            public uint lPrivate;
        }

        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", SetLastError = true)] public static extern nint OpenThread(uint access, bool inherit, uint threadId);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint handle);
        [DllImport("kernel32.dll")] private static extern bool GetThreadTimes(nint thread, out long creation, out long exit, out long kernel, out long user);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool PeekMessageW(out MSG msg, nint hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(in MSG msg);
        [DllImport("user32.dll")] public static extern nint DispatchMessageW(in MSG msg);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();

        public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(nint context);

        public static long ThreadCpu100ns(nint thread)
            => GetThreadTimes(thread, out _, out _, out long kernel, out long user) ? kernel + user : 0;
    }
}
