using Deckle.Core;

namespace Deckle.Transcription.Parakeet;

// ── ParakeetModelCatalog ─────────────────────────────────────────────────────
//
// The files of the Parakeet model Deckle provisions, and the gesture that fetches
// them: where each file sits in the pinned repository, its size and its SHA-256.
// Same role as SileroVadModel, for a model that is a folder instead of a file.
//
// The model is Olicorne's ONNX export of moondream/parakeet-ultra, itself a
// post-training of nvidia/parakeet-tdt-0.6b-v3 (CC-BY-4.0, see NOTICE.md). The
// download is pinned to one commit of that repository, so the URLs, sizes and
// hashes cannot move under an install. Sizes and hashes come from the repository's
// LFS pointers (the file listing of the pinned revision); vocab.txt is not an LFS
// file, so its hash was computed on the downloaded bytes. Take both together from
// the new revision when the pin moves.
//
// Only the FP32 files are fetched: the encoder runs in FP32 unless the person opts
// into FP16 (ParakeetEncoderPrecision), which costs fidelity on French. The mel
// front end of the repository is not used, Deckle ships its own (ParakeetModelFiles),
// and the quantized variants are left out on purpose.

/// <summary>One file of the Parakeet model repository.</summary>
/// <param name="Path">Path in the repository, forward slashes. It is also the path inside the model folder.</param>
/// <param name="SizeBytes">Exact size; the pin makes it stable.</param>
/// <param name="Sha256">Lowercase hex digest the download is verified against.</param>
public sealed record ParakeetModelFile(string Path, long SizeBytes, string Sha256)
{
    /// <summary>Download address, pinned to the catalog's revision.</summary>
    public string Url => $"{ParakeetModelCatalog.RepositoryUrl}/resolve/{ParakeetModelCatalog.Revision}/{Path}";

    /// <summary>Where the file lands inside <paramref name="modelFolder"/>.</summary>
    public string LocalPath(string modelFolder) =>
        System.IO.Path.Combine(modelFolder, Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
}

public static class ParakeetModelCatalog
{
    public const string Repository = "Olicorne/parakeet-tdt-0.6b-v3-ultra-onnx";

    /// <summary>Commit of <see cref="Repository"/> every file is read from.</summary>
    public const string Revision = "db1fd7a2d89672e8e52383505f0149076faab6f7";

    public const string RepositoryUrl = "https://huggingface.co/" + Repository;

    /// <summary>The repository at the pinned commit, for a person who downloads the files by hand.</summary>
    public const string BrowseUrl = RepositoryUrl + "/tree/" + Revision;

    /// <summary>The files the default configuration needs: vocabulary and FP32 encoder and decoder-joint with their weights.</summary>
    public static IReadOnlyList<ParakeetModelFile> Files { get; } =
    [
        new("vocab.txt", 93_939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
        new("fp32/encoder-model.onnx", 503_828, "af8d232c654c696eb07a3cf9fa164a9649209b759409e6ec9913f8155d255a23"),
        new("fp32/encoder-model.onnx.data.000", 1_483_313_152, "2762ecdbc59be479843dca0140e49300fd6e98ec994e19d0a7003308f0b3282d"),
        new("fp32/encoder-model.onnx.data.001", 952_109_056, "d26ce092d3291e893f79794cb06306f6d18390dee332330d68fc4b096b4762f3"),
        new("fp32/decoder_joint-model.onnx", 9_296, "7fa05c5183dfc89dbb0f34d4b2e7935f877762f2438bbcc79729f4e2d00eb041"),
        new("fp32/decoder_joint-model.onnx.data", 72_514_072, "5d6873a9bf49b704270a3e3dcc6c71090e1edd3248c6499b5d3cadd9f23dbcec"),
    ];

    /// <summary>The lighter encoder, for the person who opts into FP16. Not part of <see cref="Files"/>: nothing downloads it by default.</summary>
    public static ParakeetModelFile Fp16Encoder { get; } =
        new("fp16/encoder-model.fp16.onnx", 1_218_170_745, "f8353ed512daff07a3ca60e0845af2893c435325911fa8b4d21f9d63aa80250b");

    /// <summary>Size of <see cref="Files"/>, for the wizard's download estimate.</summary>
    public static long TotalBytes { get; } = Files.Sum(file => file.SizeBytes);

    /// <summary>True when every file the default configuration needs is in <paramref name="modelFolder"/>.</summary>
    public static bool IsInstalled(string modelFolder) =>
        new ParakeetModelFiles(modelFolder).IsComplete;

    /// <summary>Fetches the missing files of <see cref="Files"/> into <paramref name="modelFolder"/>, each verified against its hash.</summary>
    /// <remarks>
    /// A file already there at its pinned size stays: the downloader renames a file into place only after its
    /// hash verified, so a retry after a failure resumes at the first file that is not on disk instead of
    /// fetching 1.4 GB again. Progress covers the whole set.
    /// </remarks>
    public static Task<ProvisioningResult> ProvisionAsync(
        string modelFolder,
        IProgress<Downloader.DownloadProgress>? progress,
        CancellationToken ct) =>
        ProvisionAsync(modelFolder, Files, progress, Downloader.DownloadAsync, ct);

    // The file list and the download step are parameters so tests run the
    // sequencing on a small list without a network.
    internal static async Task<ProvisioningResult> ProvisionAsync(
        string modelFolder,
        IReadOnlyList<ParakeetModelFile> files,
        IProgress<Downloader.DownloadProgress>? progress,
        Func<string, string, string?, IProgress<Downloader.DownloadProgress>?, CancellationToken, Task<Downloader.DownloadResult>> download,
        CancellationToken ct)
    {
        long total = files.Sum(file => file.SizeBytes);
        long completed = 0;

        foreach (ParakeetModelFile file in files)
        {
            string destination = file.LocalPath(modelFolder);

            if (HasPinnedSize(destination, file))
            {
                completed += file.SizeBytes;
                progress?.Report(new Downloader.DownloadProgress(completed, total));
                continue;
            }

            long before = completed;
            var fileProgress = progress is null
                ? null
                : new InlineProgress(p => progress.Report(new Downloader.DownloadProgress(before + p.BytesDownloaded, total)));

            Downloader.DownloadResult result = await download(file.Url, destination, file.Sha256, fileProgress, ct)
                .ConfigureAwait(false);
            if (!result.Success)
                return ProvisioningResult.Fail($"{file.Path}: {result.ErrorMessage ?? "download failed"}");

            completed += file.SizeBytes;
        }

        return ProvisioningResult.Ok(completed);
    }
    private static bool HasPinnedSize(string path, ParakeetModelFile file)
    {
        try { return File.Exists(path) && new FileInfo(path).Length == file.SizeBytes; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // Progress<T> posts to the captured context and defers; the wrapper runs inline, so the caller's
    // Progress<T> still does the one hop to the UI thread.
    private sealed class InlineProgress(Action<Downloader.DownloadProgress> report) : IProgress<Downloader.DownloadProgress>
    {
        public void Report(Downloader.DownloadProgress value) => report(value);
    }
}
