namespace Deckle.Autocorrect.Mlm;

// ── CamembertAssets ───────────────────────────────────────────────────────────
//
// Identity of the CamemBERT reranker's on-disk assets: the model folder name,
// and the pinned file set the provisioning step downloads into it. The pins
// (immutable HuggingFace revision URLs would be ideal; `main` is what the
// Xenova export publishes, so each file carries its measured SHA-256 instead —
// a silent upstream change fails the checksum rather than corrupting the
// reranker). Sizes feed the wizard's estimate and progress display.
//
// The scorer itself (CamembertMlmScorer) stays path-agnostic — callers compose
// <ModelsDirectory>\<DirectoryName> and hand it the folder. IsInstalled checks
// the three files the scorer actually opens; the config/tokenizer-config
// sidecars are staged for completeness but their absence does not gate.
public static class CamembertAssets
{
    // Folder name under the shared models directory — the single name
    // App composition and the wizard both compose against.
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

    // Total download weight, for the wizard's estimate (~440 MB).
    public static long TotalSizeBytes
    {
        get
        {
            long total = 0;
            foreach (var f in Files) total += f.SizeBytes;
            return total;
        }
    }

    // The provisioning predicate: the three files the scorer opens are on
    // disk. Same shape as CamembertMlmScorer's own load-time check.
    public static bool IsInstalled(string modelDirectory) =>
        File.Exists(Path.Combine(modelDirectory, "model.onnx"))
        && File.Exists(Path.Combine(modelDirectory, "sentencepiece.bpe.model"))
        && File.Exists(Path.Combine(modelDirectory, "tokenizer.json"));
}
