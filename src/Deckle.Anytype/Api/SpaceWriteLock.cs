using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Anytype.Api;

// Cross-session exclusion for the space's mutating gestures. The Anytype REST API
// has no optimistic concurrency — no ETag, no If-Match, no 409 — and a body PATCH
// replaces the whole document, so two sessions running a read-modify-write against
// the same object overwrite one another silently. Each session runs its own host
// process, so the in-process send gate cannot coordinate them.
//
// This serializes writes across processes with an OS file lock: FileShare.None
// grants the handle to one holder at a time, and the OS releases it when the
// holder disposes the scope or — if it crashed — when its process exits. A
// mutating gesture holds the scope across its WHOLE read-modify-write, GET through
// PATCH, not just the PATCH, so no concurrent write can land in between.
public sealed class SpaceWriteLock
{
    // One lock file beside the credentials. Its contents are irrelevant; the OS
    // share mode on the open handle is the lock.
    private const string FileName = "write.lock";

    // Poll cadence while another process holds the lock. Writes are sub-second and
    // contention is rare, so a short backoff growing to a small ceiling keeps
    // latency low without busy-spinning.
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMilliseconds(200);

    private readonly string _path;

    public SpaceWriteLock(string moduleDirectory)
    {
        _path = Path.Combine(moduleDirectory, FileName);
    }

    // Acquires exclusive write access to the space, retrying until granted or
    // cancelled. operation/target name the pending write so a contended wait — two
    // sessions reaching for the same object — surfaces in the log. Dispose the
    // returned scope to release.
    public async Task<IDisposable> AcquireAsync(
        string operation, string target, CancellationToken ct = default)
    {
        TimeSpan backoff = InitialBackoff;
        long t0 = Stopwatch.GetTimestamp();
        bool waited = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // FileShare.None: the OS denies any second open — another process,
                // or another thread here — until this handle closes.
                var handle = new FileStream(
                    _path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                if (waited)
                    DeckleAnytypeSource.Log.SpaceWriteContended(
                        operation, target, Stopwatch.GetElapsedTime(t0).TotalMilliseconds);

                return handle;
            }
            catch (IOException)
            {
                // Held by another writer (sharing violation). Wait and retry; the
                // holder releases on dispose, or on process exit if it crashed.
                waited = true;
                await Task.Delay(backoff, ct).ConfigureAwait(false);
                backoff = TimeSpan.FromMilliseconds(
                    Math.Min(backoff.TotalMilliseconds * 2, MaxBackoff.TotalMilliseconds));
            }
        }
    }
}
