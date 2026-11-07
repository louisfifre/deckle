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

    // No OgaHandle, here or anywhere in the process. Its constructor does
    // nothing native; its Dispose and its finalizer call OgaShutdown, which
    // tears down ONNX Runtime GenAI's process-wide state (the ORT environment,
    // the DirectML device). Every later Model construction in the process
    // then reads freed state and dies on an access violation no catch can
    // stop: a domain-pack rebuild, which releases this judge and loads the
    // next, crashed the app that way. Without a handle that state lives until
    // the process exits; measured with 0.13.0, the process exits cleanly.
    private readonly Config _config;
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly double _margin;
    private readonly int _vocabSize;
    private readonly int? _bosTokenId;
    private readonly string? _chatTemplate;
    private readonly string _executionProvider;
    private int _chatTemplateFallbackReported;

    public string ExecutionProvider => _executionProvider;
}
