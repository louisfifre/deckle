using System.IO;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Mlm;

// ── CamembertReranker ────────────────────────────────────────────────────────
//
// The module's public seam: builds an ISentenceReranker backed by the CamemBERT
// masked-LM so the App composition root can wire the live post-sentence pass
// without reaching the internal scorer. Loading is heavy — the 442 MB ONNX model
// plus the tokenizer — and synchronous, so call it OFF the UI / input thread,
// exactly as App.Autocorrect already loads the lexicons on the thread pool.
public static class CamembertReranker
{
    // Builds the reranker from a model directory holding model.onnx,
    // tokenizer.json and sentencepiece.bpe.model. Returns null when the model is
    // absent or fails to load, so the engine runs without the contextual stage
    // rather than failing — the same graceful degradation the lexicons get.
    // `margin` is the top-vs-second logit gap required to act; `freqPrior` weights
    // the prefer-the-common-form prior (0 recovers pure-logit behaviour).
    public static ISentenceReranker? TryLoad(string modelDir, double margin, double freqPrior)
    {
        try
        {
            if (!File.Exists(Path.Combine(modelDir, "model.onnx")))
                return null;
            return new CamembertSentenceReranker(modelDir, margin, freqPrior);
        }
        catch
        {
            // A malformed model, a missing tokenizer file, or an OnnxRuntime load
            // failure: degrade to no contextual stage, never crash the engine.
            return null;
        }
    }
}
