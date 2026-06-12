namespace Deckle.Input;

// Reassembles contact frames from the report stream — the hybrid-mode
// rule from the Precision Touchpad spec: the first report of a frame
// declares the frame's total contact count, every following report of
// the same frame declares 0, and all reports of one frame share the same
// scan time. A frame is complete when the declared number of contacts
// has been gathered.
//
// Most frames fit in a single report (slots ≥ contacts) and pass through
// untouched; the accumulation path only engages when the device actually
// fragments. Valid contacts occupy the leading slots of a report, so the
// assembler takes from the front and ignores trailing padding slots.
//
// Anomalies (an orphan continuation, a frame opened before the previous
// one completed, a scan-time mismatch inside a frame) are dropped or
// flushed and counted — the counters feed the host's periodic rollup so
// real Bluetooth sessions reveal how the device actually behaves.
public sealed class ContactFrameAssembler
{
    private readonly List<TouchpadContact> _pending = new();
    private int _pendingTarget;
    private uint _pendingScanTime;
    private bool _pendingButton;
    private int _pendingReports;

    public long OrphanContinuations { get; private set; }
    public long IncompleteFlushes   { get; private set; }
    public long ScanTimeMismatches  { get; private set; }

    /// <summary>
    /// Feeds one decoded report; returns the completed frame when this
    /// report closes one, null while a fragmented frame is still
    /// accumulating (or the report had to be dropped).
    /// </summary>
    public ContactFrame? Add(TouchpadReport report, double timestampMs)
    {
        if (report.ContactCount > 0)
        {
            // A declared count opens a frame. If one was still pending,
            // the device never completed it — flush the loss and move on.
            if (_pendingTarget > 0)
            {
                IncompleteFlushes++;
                Reset();
            }

            if (report.Contacts.Length >= report.ContactCount)
            {
                // Whole frame in one report — the nominal path.
                return new ContactFrame(
                    Take(report.Contacts, report.ContactCount),
                    report.ContactCount,
                    report.ButtonDown,
                    report.ScanTime,
                    timestampMs,
                    ReportCount: 1);
            }

            _pendingTarget   = report.ContactCount;
            _pendingScanTime = report.ScanTime;
            _pendingButton   = report.ButtonDown;
            _pendingReports  = 1;
            _pending.AddRange(report.Contacts);
            return null;
        }

        // Continuation (count == 0) — only meaningful inside a frame.
        if (_pendingTarget == 0)
        {
            OrphanContinuations++;
            return null;
        }

        if (report.ScanTime != _pendingScanTime)
        {
            // Not the same frame after all — the opened frame is lost.
            ScanTimeMismatches++;
            Reset();
            return null;
        }

        _pendingReports++;
        int needed = _pendingTarget - _pending.Count;
        for (int i = 0; i < report.Contacts.Length && i < needed; i++)
            _pending.Add(report.Contacts[i]);

        if (_pending.Count < _pendingTarget) return null;

        var frame = new ContactFrame(
            _pending.ToArray(),
            _pendingTarget,
            _pendingButton || report.ButtonDown,
            _pendingScanTime,
            timestampMs,
            _pendingReports);
        Reset();
        return frame;
    }

    private static TouchpadContact[] Take(TouchpadContact[] contacts, int count)
    {
        if (contacts.Length == count) return contacts;
        var taken = new TouchpadContact[count];
        Array.Copy(contacts, taken, count);
        return taken;
    }

    private void Reset()
    {
        _pending.Clear();
        _pendingTarget   = 0;
        _pendingScanTime = 0;
        _pendingButton   = false;
        _pendingReports  = 0;
    }
}
