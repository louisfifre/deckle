using Deckle.Core;

namespace Deckle.Autocorrect.Mlm;

// ── CamembertAssets ───────────────────────────────────────────────────────────
//
// Identity of the CamemBERT reranker's on-disk assets: the model folder name and
// the pinned file set the provisioning step downloads into it. The Xenova export
// publishes on `main`, not on an immutable revision URL, so each file carries its
// SHA-256: a silent upstream change fails the checksum rather than corrupting the
// reranker.
public static class CamembertAssets
{
    // Folder name under the shared models directory, the single name callers
    // compose against.
    public const string DirectoryName = "camembert-base";

    public sealed record AssetFile(
        string FileName,
        string Url,
        string Sha256,
        long SizeBytes);

    private const string BaseUrl = "https://huggingface.co/Xenova/camembert-base/resolve/main";

    // The pinned file set, largest first so the wizard's progress bar tracks
    // the download that actually takes time.
    public static IReadOnlyList<AssetFile> Files { get; } =
    [
        new("model.onnx",
            $"{BaseUrl}/onnx/model.onnx",
            "cebea6d07c51e2834d1eec76cce1257023c18a3d7b319499e4c835561288512e",
            442_905_976L),
        new("tokenizer.json",
            $"{BaseUrl}/tokenizer.json",
            "8a10e1cc766d776b0682caa224bb8592b8cea4735128676b5164d5997bc33474",
            2_418_800L),
        new("sentencepiece.bpe.model",
            $"{BaseUrl}/sentencepiece.bpe.model",
            "988bc5a00281c6d210a5d34bd143d0363741a432fefe741bf71e61b1869d4314",
            810_912L),
        new("config.json",
            $"{BaseUrl}/config.json",
            "5c8f07e8262526c4df53da0d53f5ba175d6997e56224e2c1098231040f130f9b",
            678L),
        new("tokenizer_config.json",
            $"{BaseUrl}/tokenizer_config.json",
            "d67d4767f67c42401d0141ecf512dde63217b9d9681fa85dcabf43873090fe25",
            491L),
        new("special_tokens_map.json",
            $"{BaseUrl}/special_tokens_map.json",
            "cbe6c204e884a6f86e32acfd01bff06fdcd8b1baac8d8f0489de8ffd05a692bd",
            354L),
    ];

    public static long TotalSizeBytes
    {
        get
        {
            long total = 0;
            foreach (var f in Files) total += f.SizeBytes;
            return total;
        }
    }

    // Checks only the three files the scorer opens; the config sidecars are
    // staged for completeness but their absence does not gate.
    public static bool IsInstalled(string modelDirectory) =>
        File.Exists(Path.Combine(modelDirectory, "model.onnx"))
        && File.Exists(Path.Combine(modelDirectory, "sentencepiece.bpe.model"))
        && File.Exists(Path.Combine(modelDirectory, "tokenizer.json"));

    public static async Task<ProvisioningResult> ProvisionAsync(
        string modelDirectory,
        IProgress<Downloader.DownloadProgress> progress,
        CancellationToken ct)
    {
        long completedBytes = 0;
        foreach (AssetFile file in Files)
        {
            long baseBytes = completedBytes;
            var cumulativeProgress = new Progress<Downloader.DownloadProgress>(p =>
                progress.Report(new Downloader.DownloadProgress(
                    baseBytes + p.BytesDownloaded, TotalSizeBytes)));

            string destination = Path.Combine(modelDirectory, file.FileName);
            Downloader.DownloadResult download = await Downloader.DownloadAsync(
                file.Url, destination, file.Sha256, cumulativeProgress, ct);
            if (!download.Success)
                return ProvisioningResult.Fail(
                    $"{file.FileName}: {download.ErrorMessage ?? "download failed"}");

            completedBytes += file.SizeBytes;
        }

        return ProvisioningResult.Ok(TotalSizeBytes);
    }
}
