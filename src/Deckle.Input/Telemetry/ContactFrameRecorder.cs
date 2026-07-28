using System.Globalization;
using System.Text;
using Deckle.Core;

namespace Deckle.Input;

// Writes raw contact frames to a dedicated JSONL file — one file per
// recording session under <UserDataRoot>/telemetry/trackpad/, separate from the
// app.jsonl pipeline by design: frames flow at report cadence and would
// drown the shared log; here they land in a self-describing dataset
// made to be replayed and analyzed (cadence, gaps, bursts, hybrid
// fragmentation) before the recognizer's behavior gets designed against
// real Bluetooth sessions.
//
// File layout: a `session` header line (session id, start time), a
// `device` line per touchpad seen (capabilities, so the file stands
// alone), then one compact line per frame:
//   {"t":12.3,"dev":0,"scan":456,"n":3,"tips":3,"btn":0,"reports":1,
//    "c":[[id,x,y,tip,confidence],…]}
// `t` is milliseconds since the first recorded frame (host monotonic
// clock, 0.1 ms precision); `scan` is the device clock in 100 µs units.
//
// Start/Stop come from the settings toggle (UI thread), OnFrame from the
// input thread — every entry point takes the lock, the per-frame cost is
// string building into a reused buffer plus a buffered write.
public sealed class ContactFrameRecorder : IDisposable
{
    private const double FlushPeriodMs = 500;

    private readonly object _lock = new();
    private readonly StringBuilder _line = new(256);
    private readonly Dictionary<IntPtr, (int Index, TouchpadCapabilities Capabilities)> _devices = [];

    private StreamWriter? _writer;
    private string? _path;
    private bool _armed;
    private int _nextDeviceIndex;
    private long _frames;
    private double _firstFrameMs = -1;
    private double _lastFlushMs;

    public bool IsRecording
    {
        get { lock (_lock) return _armed; }
    }

    /// <summary>
    /// Arms a capture session. An empty <paramref name="touchpads"/> collection
    /// is fine — the JSONL file opens only on the first actual frame.
    /// </summary>
    public void Start(IReadOnlyList<TouchpadDevice> touchpads)
    {
        lock (_lock)
        {
            if (_armed) return;
            _devices.Clear();
            _nextDeviceIndex = 0;
            foreach (TouchpadDevice touchpad in touchpads)
                RegisterDevice(touchpad);
            _armed = true;
        }
    }

    /// <summary>Records the capabilities of a touchpad that just became available.</summary>
    public void NoteDevice(TouchpadDevice touchpad)
    {
        lock (_lock)
        {
            if (!_armed) return;
            try
            {
                bool added = RegisterDevice(touchpad);
                if (added && _writer is not null)
                {
                    var registered = _devices[touchpad.Handle];
                    WriteDeviceLine(registered.Index, registered.Capabilities);
                }
            }
            catch (Exception ex)
            {
                FailAndClose(ex);
            }
        }
    }

    /// <summary>Appends one frame line. Called on the input thread.</summary>
    public void OnFrame(ContactFrame frame)
    {
        lock (_lock)
        {
            if (!_armed) return;

            try
            {
                if (_writer is null && !OpenSession()) return;
                var writer = _writer!;

                if (_firstFrameMs < 0)
                {
                    _firstFrameMs = frame.TimestampMs;
                    _lastFlushMs = frame.TimestampMs;
                }

                double t = frame.TimestampMs - _firstFrameMs;
                int deviceIndex = _devices.TryGetValue(frame.DeviceHandle, out var device)
                    ? device.Index
                    : -1;

                _line.Clear();
                _line.Append("{\"t\":").Append(t.ToString("F1", CultureInfo.InvariantCulture));
                _line.Append(",\"dev\":").Append(deviceIndex);
                _line.Append(",\"scan\":").Append(frame.ScanTime);
                _line.Append(",\"n\":").Append(frame.ContactCount);
                _line.Append(",\"tips\":").Append(frame.TipCount);
                _line.Append(",\"btn\":").Append(frame.ButtonDown ? 1 : 0);
                _line.Append(",\"reports\":").Append(frame.ReportCount);
                _line.Append(",\"c\":[");
                for (int i = 0; i < frame.Contacts.Length; i++)
                {
                    var c = frame.Contacts[i];
                    if (i > 0) _line.Append(',');
                    _line.Append('[').Append(c.Id)
                         .Append(',').Append(c.X)
                         .Append(',').Append(c.Y)
                         .Append(',').Append(c.Tip ? 1 : 0)
                         .Append(',').Append(c.Confidence ? 1 : 0)
                         .Append(']');
                }
                _line.Append("]}");

                writer.WriteLine(_line);
                _frames++;

                if (frame.TimestampMs - _lastFlushMs >= FlushPeriodMs)
                {
                    writer.Flush();
                    _lastFlushMs = frame.TimestampMs;
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
            long frames = _frames;
            double durationSec = _firstFrameMs >= 0
                ? Math.Round((RawInputHost.NowMs - _firstFrameMs) / 1000.0, 1)
                : 0;

            try
            {
                _writer.Flush();
            }
            catch (Exception ex)
            {
                DeckleInputSource.Log.RecordingFailed();
                DeckleInputSource.Log.RecordingFailedDetail(ex.GetType().Name, ex.Message);
            }
            finally
            {
                _writer.Dispose();
                _writer = null;
                _path = null;
            }

            long bytes = 0;
            try { bytes = new FileInfo(path).Length; } catch { /* size is informative only */ }

            DeckleInputSource.Log.RecordingStopped();
            DeckleInputSource.Log.RecordingStoppedDetail(path, frames, durationSec, bytes);
        }
    }

    public void Dispose() => Stop();

    private bool OpenSession()
    {
        try
        {
            string fileName = $"trackpad-frames-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
            _path = Path.Combine(AppPaths.TrackpadTelemetryDirectory, fileName);
            _writer = new StreamWriter(_path, append: false, Encoding.UTF8) { AutoFlush = false };
            _frames = 0;
            _firstFrameMs = -1;
            _lastFlushMs = 0;

            _writer.WriteLine(
                $"{{\"type\":\"session\",\"schema\":2,\"session\":\"{Deckle.Diagnostics.DeckleEventSource.SessionId}\"," +
                $"\"started\":\"{DateTime.Now.ToString("o", CultureInfo.InvariantCulture)}\"}}");
            foreach (var device in _devices.Values.OrderBy(device => device.Index))
                WriteDeviceLine(device.Index, device.Capabilities);
            _writer.Flush();

            DeckleInputSource.Log.RecordingStarted();
            DeckleInputSource.Log.RecordingStartedDetail(_path);
            return true;
        }
        catch (Exception ex)
        {
            DeckleInputSource.Log.RecordingFailed();
            DeckleInputSource.Log.RecordingFailedDetail(ex.GetType().Name, ex.Message);
            _writer?.Dispose();
            _writer = null;
            _path = null;
            _armed = false;
            return false;
        }
    }

    private bool RegisterDevice(TouchpadDevice touchpad)
    {
        if (_devices.TryGetValue(touchpad.Handle, out var existing))
        {
            _devices[touchpad.Handle] = (existing.Index, touchpad.Capabilities);
            return false;
        }

        _devices.Add(touchpad.Handle, (_nextDeviceIndex++, touchpad.Capabilities));
        return true;
    }

    private void WriteDeviceLine(int index, TouchpadCapabilities c)
    {
        string name = c.DeviceName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _writer!.WriteLine(
            $"{{\"type\":\"device\",\"dev\":{index},\"name\":\"{name}\"," +
            $"\"vid\":{c.VendorId},\"pid\":{c.ProductId}," +
            $"\"x\":[{c.XMin},{c.XMax}],\"y\":[{c.YMin},{c.YMax}]," +
            $"\"slots\":{c.ContactSlots},\"reportBytes\":{c.ReportByteLength}}}");
    }

    private void FailAndClose(Exception ex)
    {
        DeckleInputSource.Log.RecordingFailed();
        DeckleInputSource.Log.RecordingFailedDetail(ex.GetType().Name, ex.Message);
        _writer?.Dispose();
        _writer = null;
        _path = null;
        _armed = false;
    }
}
