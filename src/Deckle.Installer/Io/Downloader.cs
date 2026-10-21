using System.Net.Http;
using System.Security.Cryptography;

namespace Deckle.Installer;

// Streams an HTTP download to disk while reporting byte progress and computing
// SHA-256 incrementally. Deliberately a stub-local re-take of Deckle.Core's
// Downloader rather than a reference to it: the stub takes no Deckle.*
// reference, so it stays small, self-contained and AOT-safe. The shared shape
// (atomic .partial → rename, streaming hash, throttled progress) is reproduced.
//
// Progress is a plain callback (downloaded, total-or-null) rather than a hard
// dependency on the window: the caller wires it to the progress window, and the
// downloader stays UI-agnostic.
internal static class Downloader
{
    private const int BufferSize = 81920;          // 80 KB, the CopyToAsync default
    private const int ProgressThrottleMs = 100;    // ~10 updates/sec, no flicker

    // The client has no overall timeout because a large payload on a slow link may
    // legitimately take an hour. What it must not do is wait forever on a link that
    // has gone silent: the window would sit on a marquee with nothing to say. So the
    // limit is on silence, not on duration, re-armed after every chunk received.
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(2);

    private static readonly HttpClient s_http = CreateClient();

    // Downloads url → destPath, returning the lowercase SHA-256 of the bytes
    // received. The hash is the caller's to verify against the expected one — the
    // download itself doesn't fail on mismatch, so the flow can report a precise
    // checksum error. Atomic: bytes go to .partial and are renamed only on a clean
    // finish; an aborted run leaves a .partial the next attempt overwrites.
    public static async Task<string> DownloadAsync(
        string url, string destPath, Action<long, long?>? onProgress, CancellationToken ct)
    {
        string partial = destPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(StallTimeout);
        long downloaded = 0;
        long? total = null;

        try
        {
            using var response = await s_http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            total = response.Content.Headers.ContentLength;

            using var network = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await using (var file = new FileStream(
                partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                byte[] buffer = new byte[BufferSize];
                long lastTick = Environment.TickCount64;
                int read;
                stall.CancelAfter(StallTimeout);
                while ((read = await network.ReadAsync(buffer.AsMemory(0, BufferSize), stall.Token).ConfigureAwait(false)) > 0)
                {
                    stall.CancelAfter(StallTimeout);
                    await file.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    downloaded += read;

                    long now = Environment.TickCount64;
                    if (onProgress is not null && now - lastTick >= ProgressThrottleMs)
                    {
                        onProgress(downloaded, total);
                        lastTick = now;
                    }
                }
            }

            onProgress?.Invoke(downloaded, total); // land the report at 100%

            File.Move(partial, destPath, overwrite: true);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Only the silence timer fired: the user did not cancel, so this must
            // surface as a failure with its cause, not pass for a quiet cancel.
            throw new TimeoutException(
                $"The download stalled: no data for {StallTimeout.TotalSeconds:0} s after {downloaded} of {total?.ToString() ?? "unknown"} bytes.");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = Timeout.InfiniteTimeSpan, // the stall timer above bounds silence; ct is the user's escape hatch
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Deckle-Installer");
        return client;
    }
}
