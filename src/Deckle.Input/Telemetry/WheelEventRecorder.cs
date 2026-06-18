using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Deckle.Core;

namespace Deckle.Input;

// Writes raw mouse-wheel events to a dedicated JSONL file — one file per
// recording session under <UserDataRoot>/telemetry/mouse-wheel/, the wheel
// counterpart of ContactFrameRecorder. The two share a clock
// (RawInputHost.NowMs) and a layout on purpose: the measurement package
// compares wheel cadence against trackpad gesture cadence, and that comparison
// is only honest if both streams carry the same `t` and the same
// self-describing header.
//
// File layout: a `session` header line (session id, start time), a `device`
// line per wheel-emitting device seen (a short index, the HID name, and the
// vid/pid parsed out of it, so the file stands alone), then one compact line
// per event:
//   {"t":12.3,"dev":0,"axis":"v","d":120,"gap":45.6}
// `t` is milliseconds since the first recorded event (host monotonic clock,
// 0.1 ms precision); `axis` is "v" (vertical) or "h" (horizontal); `d` is the
// signed detent from RAWMOUSE.usButtonData; `gap` is milliseconds since the
// previous event of the SAME device — the cadence the gesture model reads,
// 0 for the first event of a device. Burst grouping is left to the offline
// analysis, which can sweep thresholds the capture must not pre-decide.
//
// Start/Stop come from the settings toggle (UI thread), OnWheel from the
// input thread — every entry point takes the lock.
public sealed class WheelEventRecorder : IDisposable
{
    private const double FlushPeriodMs = 500;

    private readonly object _lock = new();
    private readonly StringBuilder _line = new(128);
    private readonly Dictionary<IntPtr, DeviceSlot> _devices = new();

    private StreamWriter? _writer;
    private string? _path;
    private bool _armed;
    private long _events;
    private double _firstEventMs = -1;
    private double _lastFlushMs;

    public bool IsRecording
    {
        get { lock (_lock) return _armed; }
    }

    /// <summary>Arms a capture session. The JSONL file opens on the first actual wheel event.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_armed) return;
            _armed = true;
        }
    }

    /// <summary>Appends one wheel event. Called on the input thread.</summary>
    public void OnWheel(MouseWheelEvent e)
    {
        lock (_lock)
        {
            if (!_armed) return;

            try
            {
                if (_writer is null && !OpenSession()) return;
                var writer = _writer!;

                if (_firstEventMs < 0)
                {
                    _firstEventMs = e.TimestampMs;
                    _lastFlushMs = e.TimestampMs;
                }

                var slot = ResolveDevice(e.Device);
                double gap = slot.LastEventMs < 0 ? 0 : e.TimestampMs - slot.LastEventMs;
                slot.LastEventMs = e.TimestampMs;

                double t = e.TimestampMs - _firstEventMs;

                _line.Clear();
                _line.Append("{\"t\":").Append(t.ToString("F1", CultureInfo.InvariantCulture));
                _line.Append(",\"dev\":").Append(slot.Index);
                _line.Append(",\"src\":\"").Append(e.Source == WheelEventSource.MessageHook ? "hook" : "raw").Append('"');
                _line.Append(",\"axis\":\"").Append(e.Axis == WheelAxis.Vertical ? 'v' : 'h').Append('"');
                _line.Append(",\"d\":").Append(e.Delta);
                _line.Append(",\"gap\":").Append(gap.ToString("F1", CultureInfo.InvariantCulture));
                _line.Append('}');

                writer.WriteLine(_line);
                _events++;

                if (e.TimestampMs - _lastFlushMs >= FlushPeriodMs)
                {
                    writer.Flush();
                    _lastFlushMs = e.TimestampMs;
                }
            }
            catch (Exception ex)
            {
                FailAndClose(ex);
            }
        }
    }

    /// <summary>Flushes and closes the current session file.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_armed) return;
            _armed = false;
            if (_writer is null) return;

            string path = _path!;
            long events = _events;
            double durationSec = _firstEventMs >= 0
                ? Math.Round((RawInputHost.NowMs - _firstEventMs) / 1000.0, 1)
                : 0;

            try
            {
                _writer.Flush();
            }
            catch (Exception ex)
            {
                DeckleInputSource.Log.WheelRecordingFailed();
                DeckleInputSource.Log.WheelRecordingFailedDetail(ex.GetType().Name, ex.Message);
            }
            finally
            {
                _writer.Dispose();
                _writer = null;
                _path = null;
            }

            long bytes = 0;
            try { bytes = new FileInfo(path).Length; } catch { /* size is informative only */ }

            DeckleInputSource.Log.WheelRecordingStopped();
            DeckleInputSource.Log.WheelRecordingStoppedDetail(path, events, durationSec, bytes);
        }
    }

    public void Dispose() => Stop();

    private bool OpenSession()
    {
        try
        {
            string fileName = $"wheel-events-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
            _path = Path.Combine(AppPaths.MouseWheelTelemetryDirectory, fileName);
            _writer = new StreamWriter(_path, append: false, Encoding.UTF8) { AutoFlush = false };
            _events = 0;
            _firstEventMs = -1;
            _lastFlushMs = 0;
            _devices.Clear();

            _writer.WriteLine(
                $"{{\"type\":\"session\",\"session\":\"{Deckle.Diagnostics.DeckleEventSource.SessionId}\"," +
                $"\"started\":\"{DateTime.Now.ToString("o", CultureInfo.InvariantCulture)}\"}}");
            _writer.Flush();

            DeckleInputSource.Log.WheelRecordingStarted();
            DeckleInputSource.Log.WheelRecordingStartedDetail(_path);
            return true;
        }
        catch (Exception ex)
        {
            DeckleInputSource.Log.WheelRecordingFailed();
            DeckleInputSource.Log.WheelRecordingFailedDetail(ex.GetType().Name, ex.Message);
            _writer?.Dispose();
            _writer = null;
            _path = null;
            _armed = false;
            return false;
        }
    }

    // First sight of a device: assign a stable short index, resolve its HID
    // name, parse the vid/pid out of it, and write the `device` line so the
    // file is self-describing. Caller holds _lock.
    private DeviceSlot ResolveDevice(IntPtr handle)
    {
        if (_devices.TryGetValue(handle, out var existing)) return existing;

        int index = _devices.Count;
        string name = handle == IntPtr.Zero ? "(mouse hook)" : TryGetDeviceName(handle) ?? "(unknown)";
        (uint vid, uint pid) = handle == IntPtr.Zero ? (0, 0) : ParseVidPid(name);

        string escaped = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _writer!.WriteLine(
            $"{{\"type\":\"device\",\"dev\":{index},\"name\":\"{escaped}\",\"vid\":{vid},\"pid\":{pid}}}");
        DeckleInputSource.Log.WheelDeviceObserved(index, name, vid, pid);

        var slot = new DeviceSlot(index);
        _devices[handle] = slot;
        return slot;
    }

    private void FailAndClose(Exception ex)
    {
        DeckleInputSource.Log.WheelRecordingFailed();
        DeckleInputSource.Log.WheelRecordingFailedDetail(ex.GetType().Name, ex.Message);
        _writer?.Dispose();
        _writer = null;
        _path = null;
        _armed = false;
    }

    // RIDI_DEVICENAME, the same two-call pattern the touchpad path uses: a
    // sizing call, then the read into a sized buffer.
    private static string? TryGetDeviceName(IntPtr handle)
    {
        uint chars = 0;
        if (RawInputInterop.GetRawInputDeviceInfo(
                handle, RawInputInterop.RIDI_DEVICENAME, IntPtr.Zero, ref chars) == unchecked((uint)-1)
            || chars == 0)
            return null;

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)chars * sizeof(char)));
        try
        {
            uint result = RawInputInterop.GetRawInputDeviceInfo(
                handle, RawInputInterop.RIDI_DEVICENAME, buffer, ref chars);
            return result == unchecked((uint)-1) ? null : Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // The HID device-interface path carries the ids literally, e.g.
    // \\?\HID#VID_046D&PID_C548&... — pull the four hex digits after each
    // marker. Returns (0, 0) when absent (a non-HID or unnamed device).
    private static (uint Vid, uint Pid) ParseVidPid(string name)
    {
        return (ParseHexAfter(name, "VID_"), ParseHexAfter(name, "PID_"));
    }

    private static uint ParseHexAfter(string text, string marker)
    {
        int at = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0 || at + marker.Length + 4 > text.Length) return 0;
        return uint.TryParse(
            text.AsSpan(at + marker.Length, 4),
            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)
            ? value
            : 0;
    }

    private sealed class DeviceSlot(int index)
    {
        public int Index { get; } = index;
        public double LastEventMs { get; set; } = -1;
    }
}
