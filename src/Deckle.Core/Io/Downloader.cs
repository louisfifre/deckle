using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Deckle.Core;

// ── Downloader ───────────────────────────────────────────────────────────────
//
// Streams an HTTP download to disk while reporting progress and computing
// SHA-256 incrementally.
//
// Three guarantees:
//
//   1. Atomic write — bytes go to <destPath>.partial during the transfer
//      and the file is renamed to <destPath> only after SHA-256 verifies
//      (or unconditionally when ExpectedSha256 is null). A failed or
//      cancelled download deletes its .partial; one left by a killed
//      process is overwritten by the next attempt.
//
//   2. Streaming hash — IncrementalHash digests every buffer as it's
//      written, so verification is O(1) memory and doesn't require a
//      second full read of the file.
//
//   3. Cancellation — every async call honours the CancellationToken so
//      the caller's Cancel stops the transfer in milliseconds, not at the
//      next megabyte boundary.
//
//   4. Bounded silence — a link that goes quiet ends the download after
//      StallTimeout with a readable message instead of hanging until the
//      person cancels. The timer is re-armed on every chunk, so a slow but
//      live transfer is never cut. A retry starts the file over: the
//      .partial is not resumed.
//
// HttpClient is reused as a static field (Microsoft Learn guidance:
// instantiating per-call exhausts sockets under load). The default 100 s
// timeout is overridden to Infinite because large model files routinely
// exceed it on slow links — the stall timer bounds silence, and the
// caller's cancellation is the escape hatch for the rest.
public static class Downloader
{
    private const int BufferSize = 81920; // 80 KB, matches Stream.CopyToAsync default

    // IProgress<T>.Report posts on the captured SyncContext (the UI
    // dispatcher when a page created the Progress instance), and a post for
    // every buffer saturates the message pump enough to freeze the window.
    private const int ProgressReportThrottleMs = 200;

    // Silence, not duration: re-armed after every chunk received.
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(2);

    private static readonly HttpClient _http = CreateHttpClient();

    public sealed record DownloadProgress(long BytesDownloaded, long? TotalBytes)
    {
        // Null when the server didn't send Content-Length (common for proxied
        // or gzipped responses); the UI then shows an indeterminate bar.
        public double? Percent => TotalBytes is > 0
            ? (double)BytesDownloaded / TotalBytes.Value
            : null;
    }

    public sealed record DownloadResult(
        bool Success,
        string? ActualSha256,
        string? ErrorMessage)
    {
        public static DownloadResult Ok(string actualSha256) => new(true, actualSha256, null);
        public static DownloadResult Fail(string message)    => new(false, null, message);
    }

    public static Task<DownloadResult> DownloadAsync(
        string url,
        string destPath,
        string? expectedSha256,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
        => DownloadAsync(_http, StallTimeout, url, destPath, expectedSha256, progress, ct);

    // The seam the tests use: their own HttpClient and a short silence limit.
    internal static async Task<DownloadResult> DownloadAsync(
        HttpClient http,
        TimeSpan stallTimeout,
        string url,
        string destPath,
        string? expectedSha256,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return DownloadResult.Fail("download URL is empty");

        string partialPath = destPath + ".partial";
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long downloaded = 0;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            stall.CancelAfter(stallTimeout);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;

            using var network = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var file = new FileStream(partialPath,
                FileMode.Create, FileAccess.Write, FileShare.None,
                BufferSize, useAsync: true))
            {
                byte[] buffer = new byte[BufferSize];
                long lastReportTicks = Environment.TickCount64;
                int read;
                while ((read = await network.ReadAsync(buffer.AsMemory(0, BufferSize), stall.Token).ConfigureAwait(false)) > 0)
                {
                    stall.CancelAfter(stallTimeout);
                    await file.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    downloaded += read;

                    long now = Environment.TickCount64;
                    if (now - lastReportTicks >= ProgressReportThrottleMs)
                    {
                        progress?.Report(new DownloadProgress(downloaded, total));
                        lastReportTicks = now;
                    }
                }
            }

            // Final tick — bar lands at 100% even if the last loop iteration
            // didn't cross the throttle threshold.
            progress?.Report(new DownloadProgress(downloaded, total));

            string actualSha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                !string.Equals(actualSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partialPath);
                return DownloadResult.Fail(
                    $"checksum mismatch: expected {expectedSha256}, got {actualSha}");
            }

            File.Move(partialPath, destPath, overwrite: true);

            return DownloadResult.Ok(actualSha);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Only the silence timer fired: the person did not cancel.
            TryDelete(partialPath);
            return DownloadResult.Fail(
                $"The download stalled: no data for {stallTimeout.TotalSeconds:0} s after {downloaded} bytes. Check the connection and try again.");
        }
        catch (OperationCanceledException)
        {
            TryDelete(partialPath);
            throw;
        }
        catch (HttpRequestException ex)
        {
            TryDelete(partialPath);
            return DownloadResult.Fail(ex.StatusCode is { } code
                ? $"The server refused the download (HTTP {(int)code})."
                : $"The connection failed: {ex.InnerException?.Message ?? ex.Message}. Check the connection and try again.");
        }
        catch (IOException ex) when (ex.InnerException is not null)
        {
            // A cut socket surfaces as an IOException wrapping the transport error.
            TryDelete(partialPath);
            return DownloadResult.Fail($"The connection was interrupted: {ex.InnerException.Message}. Check the connection and try again.");
        }
        catch (Exception ex)
        {
            TryDelete(partialPath);
            return DownloadResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true, // HuggingFace mirrors redirect to a CDN
        });
        client.Timeout = Timeout.InfiniteTimeSpan;
        // User-Agent helps shared infrastructure logs distinguish our traffic
        // from generic curl/wget.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Deckle/1.0");
        return client;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
