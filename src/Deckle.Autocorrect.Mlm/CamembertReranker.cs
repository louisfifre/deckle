using System.IO;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Mlm;

// ── CamembertReranker ────────────────────────────────────────────────────────
//
// The module's public seam: builds an ISentenceReranker backed by the CamemBERT
// masked-LM without exposing the internal scorer. Loading the ONNX model and the
// tokenizer is heavy and synchronous, so call it OFF the UI and input threads.
public static class CamembertReranker
{
    // Null when the model is absent or fails to load, so the engine runs without
    // the contextual stage rather than failing. `margin` is the top-vs-second
    // logit gap required to act; `freqPrior` weights the prefer-the-common-form
    // prior (0 recovers pure-logit behaviour).
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
