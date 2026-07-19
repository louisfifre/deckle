using System.IO;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deckle.Autocorrect;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Autocorrect.Onnx;

// Minimal ONNX Runtime GenAI scorer for closed sentence candidates. It frames
// the model as a judge, then performs forced decoding over each known candidate
// answer. The applied output remains one of the caller's candidates, never free
// generation.
public sealed partial class OnnxSentenceScorer : ISentenceScorer, IDisposable
{
    private const string LogitsOutputName = "logits";
    private const string SystemPrompt =
        "You are Deckle's local French autocorrect judge. You choose only among closed candidates.";

    private readonly OgaHandle _ogaHandle;
    private readonly Config _config;
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly double _margin;
    private readonly int _vocabSize;
    private readonly int? _bosTokenId;
    private readonly string? _chatTemplate;
    private readonly string _executionProvider;

    // The execution provider the judge model was loaded onto ("dml" for the GPU,
    // "cpu" for the built-in CPU EP) — surfaced so a run can report where it ran.
    public string ExecutionProvider => _executionProvider;
}
