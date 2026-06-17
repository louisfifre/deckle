using System.IO;
using System.Text.Json;
using Deckle.Inference.Onnx;
using Microsoft.ML.Tokenizers;

namespace Deckle.Autocorrect.Mlm;

// ── CamembertMlmScorer ──────────────────────────────────────────────────────
//
// POC core for the post-sentence reranker: loads a CamemBERT masked-LM exported
// to ONNX and, given a sentence with one masked slot, reads the fill-mask logits
// to score a CLOSED SET of candidate surface forms (e.g. "à" vs "a"). It never
// generates — it only ranks forms the lexicon already proposed, so it stays a
// correction, not a rewrite.
//
// Tokenisation guardrail. CamemBERT's ids do not follow the raw SentencePiece
// order (fairseq remap: <s>=5, </s>=6, <mask>=32004). The generic
// SentencePieceTokenizer is trusted only to SEGMENT text into pieces; each piece
// string is mapped to a model id through the vocab baked into tokenizer.json —
// the exact table the ONNX graph was exported against. That sidesteps the id
// offset entirely. Pieces absent from the vocab fall back to <unk>, counted so
// a representation mismatch shows up loudly instead of silently scoring noise.
internal sealed class CamembertMlmScorer : IDisposable
{
    // Special-token ids, read from the model's tokenizer.json added_tokens.
    private const int BosId = 5;      // <s>   (CLS, sequence start)
    private const int EosId = 6;      // </s>  (SEP, sequence end)
    private const int UnkId = 4;      // <unk>
    private const int MaskId = 32004; // <mask>

    private const char Metaspace = '▁'; // ▁ — SentencePiece's space marker

    private readonly OnnxModelSession _session;
    private readonly SentencePieceTokenizer _tokenizer;
    private readonly Dictionary<string, int> _vocab; // piece string -> model id

    public CamembertMlmScorer(string modelDir)
    {
        _session = new OnnxModelSession(Path.Combine(modelDir, "model.onnx"));

        using (Stream spm = File.OpenRead(Path.Combine(modelDir, "sentencepiece.bpe.model")))
            _tokenizer = SentencePieceTokenizer.Create(spm, false, false); // no auto BOS/EOS — we add them as ids

        _vocab = LoadVocab(Path.Combine(modelDir, "tokenizer.json"));
    }

    // The model id of a word's leading piece ("▁" + word), or -1 when the word
    // is not a single vocab piece (the caller then needs the multi-token path).
    public int LeadingPieceId(string word) =>
        _vocab.TryGetValue(Metaspace + word, out int id) ? id : -1;

    // Segments a text fragment and maps each piece to its model id. Unknown
    // pieces become <unk>; their count is reported for diagnostics.
    public int[] Encode(string text, out int unknownPieces)
    {
        int unknown = 0;
        var ids = new List<int>();
        foreach (EncodedToken token in _tokenizer.EncodeToTokens(text, out _))
        {
            if (_vocab.TryGetValue(token.Value, out int id))
                ids.Add(id);
            else { ids.Add(UnkId); unknown++; }
        }
        unknownPieces = unknown;
        return ids.ToArray();
    }

    // Runs the model on  <s> left <mask> right </s>  and returns the logits row
    // at the masked position — one score per vocabulary id. The caller reads the
    // scores of its candidate ids and picks the argmax.
    public float[] MaskLogits(IReadOnlyList<int> leftIds, IReadOnlyList<int> rightIds)
    {
        var ids = new List<int>(leftIds.Count + rightIds.Count + 3) { BosId };
        ids.AddRange(leftIds);
        int maskPos = ids.Count;
        ids.Add(MaskId);
        ids.AddRange(rightIds);
        ids.Add(EosId);

        int seq = ids.Count;
        var inputIds = new long[seq];
        var attention = new long[seq];
        for (int i = 0; i < seq; i++) { inputIds[i] = ids[i]; attention[i] = 1L; }

        float[] logits = _session.Run(new[]
        {
            OnnxTensorInput.Int64("input_ids", inputIds, new[] { 1, seq }),
            OnnxTensorInput.Int64("attention_mask", attention, new[] { 1, seq }),
        })[0]; // logits, shape [1, seq, vocab], row-major flattened

        int vocabDim = logits.Length / seq;
        var row = new float[vocabDim];
        Array.Copy(logits, maskPos * vocabDim, row, 0, vocabDim);
        return row;
    }

    // Loads tokenizer.json's Unigram vocab: an array of [piece, score], where the
    // array index IS the model id (the fast tokenizer baked the final id space in).
    private static Dictionary<string, int> LoadVocab(string tokenizerJsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(tokenizerJsonPath));
        JsonElement vocab = doc.RootElement.GetProperty("model").GetProperty("vocab");

        var map = new Dictionary<string, int>(vocab.GetArrayLength(), StringComparer.Ordinal);
        int id = 0;
        foreach (JsonElement entry in vocab.EnumerateArray())
        {
            string piece = entry[0].GetString()!;
            map[piece] = id++;
        }
        return map;
    }

    public void Dispose() => _session.Dispose();
}
