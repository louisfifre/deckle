using Deckle.Core;

namespace Deckle.Vad;

// Identity of the Silero VAD model VadService provisions and loads: file name,
// download URL, and the SHA-256 both the download and the on-disk copy are checked
// against. The URL is pinned to an immutable release tag so it never moves with
// upstream master.
public static class SileroVadModel
{
    public const string FileName  = "silero_vad.onnx";
    public const string Url       = "https://raw.githubusercontent.com/snakers4/silero-vad/v6.2/src/silero_vad/data/silero_vad.onnx";
    public const string Sha256    = "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3";
    // Stable with the pin: the URL points at an immutable release tag, so the
    // byte count cannot drift. Feeds the wizard's download estimate.
    public const long   SizeBytes = 2_327_524;

    public static bool IsInstalled(string modelDirectory) =>
        File.Exists(Path.Combine(modelDirectory, FileName));

    public static async Task<ProvisioningResult> ProvisionAsync(
        string modelDirectory,
        IProgress<Downloader.DownloadProgress>? progress,
        CancellationToken ct)
    {
        string destination = Path.Combine(modelDirectory, FileName);
        Downloader.DownloadResult download = await Downloader.DownloadAsync(
            Url, destination, Sha256, progress, ct).ConfigureAwait(false);
        return download.Success
            ? ProvisioningResult.Ok(new FileInfo(destination).Length, download.ActualSha256)
            : ProvisioningResult.Fail(download.ErrorMessage ?? "download failed");
    }
}
