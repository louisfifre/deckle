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
//   {"t":12.3,"scan":456,"n":3,"tips":3,"btn":0,"reports":1,
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

    private StreamWriter? _writer;
    private string? _path;
    private long _frames;
    private double _firstFrameMs = -1;
    private double _lastFlushMs;

    public bool IsRecording
    {
        get { lock (_lock) return _writer is not null; }
    }

    /// <summary>
    /// Opens a new session file. A null <paramref name="touchpad"/> is
    /// fine (no device yet) — a `device` line follows whenever one
    /// arrives via <see cref="NoteDevice"/>.
    /// </summary>
    public void Start(TouchpadCapabilities? touchpad)
    {
        lock (_lock)
        {
            if (_writer is not null) return;

            try
            {
                string fileName = $"trackpad-frames-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
                _path = Path.Combine(AppPaths.TrackpadTelemetryDirectory, fileName);
                _writer = new StreamWriter(_path, append: false, Encoding.UTF8) { AutoFlush = false };
                _frames = 0;
                _firstFrameMs = -1;
                _lastFlushMs = 0;

                _writer.WriteLine(
                    $"{{\"type\":\"session\",\"session\":\"{Deckle.Diagnostics.DeckleEventSource.SessionId}\"," +
                    $"\"started\":\"{DateTime.Now.ToString("o", CultureInfo.InvariantCulture)}\"}}");
                if (touchpad is not null) WriteDeviceLine(touchpad);
                _writer.Flush();

                DeckleInputSource.Log.RecordingStarted();
                DeckleInputSource.Log.RecordingStartedDetail(_path);
            }
            catch (Exception ex)
            {
                DeckleInputSource.Log.RecordingFailed();
                DeckleInputSource.Log.RecordingFailedDetail(ex.GetType().Name, ex.Message);
                _writer?.Dispose();
                _writer = null;
                _path = null;
            }
        }
    }

    /// <summary>Records the capabilities of a touchpad that just became available.</summary>
    public void NoteDevice(TouchpadCapabilities touchpad)
    {
        lock (_lock)
        {
            if (_writer is null) return;
            try
            {
                WriteDeviceLine(touchpad);
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
            if (_writer is null) return;

            try
            {
                if (_firstFrameMs < 0)
                {
                    _firstFrameMs = frame.TimestampMs;
                    _lastFlushMs = frame.TimestampMs;
                }

                double t = frame.TimestampMs - _firstFrameMs;

                _line.Clear();
                _line.Append("{\"t\":").Append(t.ToString("F1", CultureInfo.InvariantCulture));
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

                _writer.WriteLine(_line);
                _frames++;

                if (frame.TimestampMs - _lastFlushMs >= FlushPeriodMs)
                {
                    _writer.Flush();
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

    private void WriteDeviceLine(TouchpadCapabilities c)
    {
        string name = c.DeviceName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _writer!.WriteLine(
            $"{{\"type\":\"device\",\"name\":\"{name}\"," +
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
    }
}
