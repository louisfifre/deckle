using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Core;

namespace Deckle.Vad;

// Owns the Silero VAD's lifecycle: resolve the model path, verify it, load the
// session (or kick a one-time background download), and dispose. A caller hands in
// how to resolve the models directory and then asks EnsureReady() per take; the
// loaded session is cached for the service's life.
//
// Resolution is lazy and never blocks: if the model is present it loads
// synchronously (a couple of MB, milliseconds); if absent, a background download
// is started once and EnsureReady returns null — the current take runs without
// the trim and a later take picks the model up. A failed download is retried in
// that same background task, never on the caller's thread (see DownloadRetryDelays).
public sealed class VadService : IDisposable
{
    // The wait before each retry of a failed download, so a download is tried at
    // most 1 + 3 times per session. The values are a judgment, not a measurement:
    // the usual causes of a one-off failure are a network that is not up yet after
    // a login or a resume from sleep (tens of seconds), a captive portal or a DNS
    // hiccup (minutes) and a host that is down for a while (longer). Growing
    // waits give each of those a chance without hammering the host, and the whole
    // ladder (about 12 minutes) is short enough to still matter to the person who
    // is dictating now. Past it, the next start is the natural retry.
    internal static readonly TimeSpan[] DownloadRetryDelays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    private readonly Func<string> _resolveModelsDirectory;
    private readonly Func<string, Task<ProvisioningResult>> _provisionModel;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    // Cancelled by Dispose so a download that is waiting for its next attempt
    // stops waiting, instead of downloading into a service that was shut down.
    private readonly CancellationTokenSource _stopRetries = new();

    private SileroVad? _vad;
    private bool _downloadKicked;   // a background download is in flight or done
    private Task? _downloadTask;    // that download, kept so a test can await its end
    private bool _loadFailed;       // constructing the session threw — don't retry

    // The open "takes run untrimmed" incident, null when none. Guarded because the
    // background download reports its failure from a pool thread, and trim
    // outcomes arrive from the streaming consumer task.
    private readonly object _incidentLock = new();
    private string? _incidentReason;

    public VadService(Func<string> resolveModelsDirectory)
        : this(resolveModelsDirectory, ProvisionFromTheNetworkAsync, Task.Delay)
    {
    }

    // The download and the wait are the two things a test cannot do for real: one
    // reaches the network, the other takes minutes.
    internal VadService(
        Func<string> resolveModelsDirectory,
        Func<string, Task<ProvisioningResult>> provisionModel,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _resolveModelsDirectory = resolveModelsDirectory;
        _provisionModel = provisionModel;
        _delay = delay;
    }

    // Ends when the download succeeded, gave up, or was stopped by Dispose.
    internal Task DownloadEnded => _downloadTask ?? Task.CompletedTask;

    // expectedSha256 guards against a corrupt/truncated transfer: the downloader
    // verifies the bytes and discards the .partial on mismatch, so a bad file is
    // never published. CancellationToken.None is deliberate — this fire-and-forget
    // transfer (a couple of MB) outlives Dispose rather than being torn down
    // mid-flight; the OS reclaims it at exit.
    private static Task<ProvisioningResult> ProvisionFromTheNetworkAsync(string modelDirectory)
        => SileroVadModel.ProvisionAsync(modelDirectory, progress: null, CancellationToken.None);

    // Returns the loaded VAD, or null when the model isn't usable yet. Call from a
    // single thread (the streaming worker) — there is no concurrency on _vad.
    public SileroVad? EnsureReady()
    {
        if (_vad is not null) return _vad;
        if (_loadFailed) return null;

        string modelPath = ResolveModelPath();
        bool modelPresent = File.Exists(modelPath);

        if (modelPresent)
        {
            // Hash verify and session construction run at most once per service,
            // on the first streaming take's critical path; timed together.
            var sw = Stopwatch.StartNew();
            if (FileMatchesExpectedSha(modelPath, out Exception? readError))
            {
                try
                {
                    _vad = new SileroVad(modelPath);
                    sw.Stop();
                    DeckleVadSource.Log.SpeechTrimVadLoaded();
                    DeckleVadSource.Log.SpeechTrimVadLoadedDetail(modelPath);
                    DeckleVadSource.Log.SpeechTrimVadLoadComplete(sw.ElapsedMilliseconds);
                    CloseIncident();
                    return _vad;
                }
                catch (Exception ex)
                {
                    _loadFailed = true;
                    OpenIncident("load_failed", modelPath, ex.GetType().Name, ex.Message);
                    // Bytes match the pinned build yet it won't construct — should not
                    // happen, but delete defensively so a later launch retries clean.
                    TryDeleteModel(modelPath);
                    return null;
                }
            }

            // Present but not the pinned build — an older model version (so a version
            // bump actually takes effect instead of silently keeping the old file) or
            // a corrupt copy. File.Exists alone can't tell the difference; drop it and
            // fall through to the verified download.
            if (readError is null)
                OpenIncident("checksum_mismatch", modelPath, "", "");
            else
                OpenIncident("checksum_read_failed", modelPath, readError.GetType().Name, readError.Message);
            TryDeleteModel(modelPath);
        }

        if (!_downloadKicked)
        {
            // A present model already opened the incident with its own cause.
            if (!modelPresent)
                OpenIncident("model_missing", modelPath, "", "");
            _downloadKicked = true;
            _downloadTask = DownloadModelAsync(modelPath);
        }
        return null;
    }

    // The caller runs the loaded VAD itself, so it reports each trim's outcome
    // here: the trim failures of successive takes are one incident, opened by
    // the first and closed by the next trim that succeeds. Each failure stays
    // a Verbose attempt; only the opening reaches the log.
    public void ReportTrimFailure(int utteranceIndex, Exception ex)
    {
        DeckleVadSource.Log.SpeechTrimFailed(utteranceIndex, ex.GetType().Name, ex.Message);
        OpenIncident("trim_failed", ResolveModelPath(), ex.GetType().Name, ex.Message);
    }

    // Closes whatever incident is open: a trim that ran proves the VAD usable,
    // whatever made it unusable before.
    public void ReportTrimSucceeded() => CloseIncident();

    private string ResolveModelPath()
        => Path.Combine(_resolveModelsDirectory(), SileroVadModel.FileName);

    // Runs on the pool, off every caller: the first attempt, then one retry per
    // entry of DownloadRetryDelays. All the failures are one incident: the first
    // opens it (download_failed), each failure is a Verbose attempt, a success
    // closes it, and running out of attempts reports once that the next start
    // will try again.
    private async Task DownloadModelAsync(string modelPath)
    {
        int attempts = DownloadRetryDelays.Length + 1;
        for (int attempt = 1; ; attempt++)
        {
            DeckleVadSource.Log.SpeechTrimVadDownloadStart();
            DeckleVadSource.Log.SpeechTrimVadDownloadStartDetail(SileroVadModel.Url);

            (string ExType, string Message)? failure = await TryDownloadOnceAsync(modelPath)
                .ConfigureAwait(false);
            if (failure is null)
            {
                DeckleVadSource.Log.SpeechTrimVadDownloadComplete();
                DeckleVadSource.Log.SpeechTrimVadDownloadCompleteDetail(modelPath);
                // The model is on disk and verified, so the trim comes back with
                // the next take; no need to wait for that take to say so.
                CloseIncident();
                return;
            }

            OpenIncident("download_failed", modelPath, failure.Value.ExType, failure.Value.Message);

            bool last = attempt == attempts;
            TimeSpan wait = last ? TimeSpan.Zero : DownloadRetryDelays[attempt - 1];
            DeckleVadSource.Log.SpeechTrimVadDownloadAttemptFailedDetail(
                attempt, attempts, (long)wait.TotalSeconds,
                failure.Value.ExType, failure.Value.Message);

            if (last)
            {
                OpenIncident(
                    "download_gave_up", modelPath, "",
                    $"gave up after {attempts} attempts, will retry at the next start");
                return;
            }

            try
            {
                await _delay(wait, _stopRetries.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Dispose: the service is shut down, so no attempt and no line.
                return;
            }
        }
    }

    private async Task<(string ExType, string Message)?> TryDownloadOnceAsync(string modelPath)
    {
        try
        {
            ProvisioningResult result = await _provisionModel(Path.GetDirectoryName(modelPath)!)
                .ConfigureAwait(false);
            // The downloader reports a failure as text, not as an exception.
            return result.Success ? null : ("", result.ErrorMessage ?? "");
        }
        catch (Exception ex)
        {
            return (ex.GetType().Name, ex.Message);
        }
    }

    // One incident per cause: a later take that finds the VAD unusable for the
    // same reason adds nothing, a new cause is news and is reported.
    private void OpenIncident(string reason, string modelPath, string exType, string exMessage)
    {
        lock (_incidentLock)
        {
            if (_incidentReason == reason) return;
            _incidentReason = reason;
        }
        DeckleVadSource.Log.SpeechTrimUnavailable(reason, exType, exMessage);
        DeckleVadSource.Log.SpeechTrimUnavailableDetail(reason, modelPath);
    }

    private void CloseIncident()
    {
        string? previous;
        lock (_incidentLock)
        {
            previous = _incidentReason;
            _incidentReason = null;
        }
        if (previous is not null) DeckleVadSource.Log.SpeechTrimRecovered(previous);
    }

    private static void TryDeleteModel(string modelPath)
    {
        try { File.Delete(modelPath); }
        catch (Exception ex)
        {
            // Best effort: a file that stays blocks the verified download from
            // publishing, and that failure carries the visible cause.
            DeckleVadSource.Log.SpeechTrimVadFileDeleteFailed(modelPath, ex.GetType().Name, ex.Message);
        }
    }

    // Runs at most once per service (the loaded session is cached), so hashing a
    // file of a couple of MB is cheap insurance that the file on disk is exactly
    // the build we expect — not an older version or a corrupt copy. A file that
    // cannot be read or hashed counts as a mismatch so it gets replaced; the
    // caller reports readError as the cause.
    private static bool FileMatchesExpectedSha(string modelPath, out Exception? readError)
    {
        readError = null;
        try
        {
            using FileStream stream = File.OpenRead(modelPath);
            byte[] hash = SHA256.HashData(stream);
            return string.Equals(
                Convert.ToHexString(hash), SileroVadModel.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            readError = ex;
            return false;
        }
    }

    public void Dispose()
    {
        _stopRetries.Cancel();
        _vad?.Dispose();
        _vad = null;
    }
}
