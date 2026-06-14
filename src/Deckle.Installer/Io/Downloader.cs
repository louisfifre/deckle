using System.Net.Http;
using System.Security.Cryptography;

using Deckle.Installer;

namespace Deckle.Installer;

// ── Downloader ────────────────────────────────────────────────────────────────
//
// Streams an HTTP download to disk while drawing a console progress bar and
// computing SHA-256 incrementally. Deliberately a console-local re-take of
// Deckle.Transcription.Downloader rather than a reference to it: that one
// lives in a WinUI module and reports through IProgress<T> on a UI dispatcher;
// pulling it in would drag all of WinUI into a stub whose entire reason to exist
// is to stay small and self-contained. The shared shape (atomic .partial →
// rename, streaming hash, throttled progress) is reproduced, ~80 lines.
internal static class Downloader
{
    private const int BufferSize = 81920;          // 80 KB, the CopyToAsync default
    private const int ProgressThrottleMs = 100;    // ~10 bar redraws/sec, no flicker

    private static readonly HttpClient s_http = CreateClient();

    // Downloads url → destPath, returning the lowercase SHA-256 of the bytes
    // received. The hash is the caller's to verify against the expected one — the
    // download itself doesn't fail on mismatch, so the flow can report a precise
    // checksum error. Atomic: bytes go to .partial and are renamed only on a clean
    // finish; an aborted run leaves a .partial the next attempt overwrites.
    public static async Task<string> DownloadAsync(
        string url, string destPath, bool showProgress, CancellationToken ct)
    {
        string partial = destPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        using var response = await s_http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        using var network = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long downloaded = 0;

        await using (var file = new FileStream(
            partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            byte[] buffer = new byte[BufferSize];
            long lastTick = Environment.TickCount64;
            int read;
            while ((read = await network.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                downloaded += read;

                long now = Environment.TickCount64;
                if (showProgress && now - lastTick >= ProgressThrottleMs)
                {
                    ConsoleUi.ProgressBar(downloaded, total);
                    lastTick = now;
                }
            }
        }

        if (showProgress)
        {
            ConsoleUi.ProgressBar(downloaded, total); // land the bar at 100%
            ConsoleUi.ProgressDone();
        }

        File.Move(partial, destPath, overwrite: true);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    // Fetches a small text resource in full (the .sha256 sidecar). No streaming
    // ceremony — these are a few dozen bytes.
    public static async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var response = await s_http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = Timeout.InfiniteTimeSpan, // big payload on a slow link; ct is the escape hatch
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Deckle-Installer");
        return client;
    }
}
