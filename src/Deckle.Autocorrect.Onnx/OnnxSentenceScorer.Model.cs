using System.IO;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deckle.Autocorrect;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Autocorrect.Onnx;

public sealed partial class OnnxSentenceScorer
{
    public OnnxSentenceScorer(string modelDir, double margin, string executionProvider = "dml")
    {
        _margin = double.IsFinite(margin) && margin > 0.0 ? margin : 0.0;
        _vocabSize = TryReadVocabSize(modelDir) ?? 0;
        _chatTemplate = TryReadChatTemplate(modelDir);
        _executionProvider = string.IsNullOrWhiteSpace(executionProvider)
            ? "cpu"
            : executionProvider.Trim();

        OgaHandle? ogaHandle = null;
        Config? config = null;
        Model? model = null;
        Tokenizer? tokenizer = null;
        try
        {
            ogaHandle = new OgaHandle();

            (config, model) = CreateModel(modelDir, _executionProvider, out bool retried);
            tokenizer = new Tokenizer(model);

            _ogaHandle = ogaHandle;
            _config = config;
            _model = model;
            _tokenizer = tokenizer;
            _bosTokenId = TryGetBosTokenId(_tokenizer);

            DeckleAutocorrectSource.Log.SentenceJudgeLoaded(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(modelDir)),
                _executionProvider,
                _margin,
                _vocabSize,
                retried);
        }
        catch
        {
            tokenizer?.Dispose();
            model?.Dispose();
            config?.Dispose();
            ogaHandle?.Dispose();
            throw;
        }
    }

    // Builds the config and the model, with one bounded retry. The provider is
    // chosen in code, not read from the export's genai_config.json: clear the
    // config's providers and append the chosen one. "cpu" leaves the list empty →
    // the built-in CPU EP. The export must still suit the provider: a CPU int4
    // export's MatMulNBits nodes do not partition to DirectML, so a GPU run needs
    // a dedicated `-e dml` export. Model construction enumerates the DML devices,
    // and that enumeration fails transiently — "Specified provider is not
    // supported" on one run, clean on the next, same binary and machine. One retry
    // absorbs the flake for every consumer (live composition, probe, replay); a
    // second failure is a real one and propagates. The config is rebuilt per
    // attempt rather than reused across a failed native construction.
    private static (Config Config, Model Model) CreateModel(
        string modelDir,
        string executionProvider,
        out bool retried)
    {
        retried = false;
        for (int attempt = 0; ; attempt++)
        {
            Config? config = null;
            try
            {
                config = new Config(modelDir);
                config.ClearProviders();
                if (!string.Equals(executionProvider, "cpu", StringComparison.OrdinalIgnoreCase))
                    config.AppendProvider(executionProvider);

                return (config, new Model(config));
            }
            catch (OnnxRuntimeGenAIException ex) when (attempt == 0)
            {
                config?.Dispose();
                retried = true;
                DeckleAutocorrectSource.Log.RerankerLoadRetried(
                    executionProvider, ex.GetType().FullName ?? ex.GetType().Name, ex.Message);
                Thread.Sleep(250);
            }
            catch
            {
                config?.Dispose();
                throw;
            }
        }
    }

    public static ISentenceScorer? TryLoad(string modelDir, double margin, string executionProvider = "dml")
        => TryLoad(modelDir, margin, executionProvider, out _);

    // Keeps an absent optional model distinct from a present model that failed
    // to initialize. Composition can therefore preserve its fallback policy
    // while exposing the exact native/runtime failure that caused it.
    public static ISentenceScorer? TryLoad(
        string modelDir,
        double margin,
        string executionProvider,
        out Exception? error)
    {
        error = null;
        try
        {
            if (!Directory.Exists(modelDir))
                return null;

            return new OnnxSentenceScorer(modelDir, margin, executionProvider);
        }
        catch (Exception ex)
        {
            error = ex;
            return null;
        }
    }

    private static int? TryGetBosTokenId(Tokenizer tokenizer)
    {
        try
        {
            int id = tokenizer.GetBosTokenId();
            return id >= 0 ? id : null;
        }
        catch (Exception ex)
        {
            // Scoring runs on without a BOS prefix, which shifts every score the
            // calibrated margin was measured on.
            DeckleAutocorrectSource.Log.SentenceJudgeFeatureUnavailable(
                DeckleAutocorrectSource.SentenceJudgeFeatures.BosToken,
                ex.GetType().FullName ?? ex.GetType().Name,
                ex.Message);
            return null;
        }
    }

    private static int? TryReadVocabSize(string modelDir)
    {
        foreach (string name in new[] { "genai_config.json", "config.json" })
        {
            string path = Path.Combine(modelDir, name);
            if (!File.Exists(path))
                continue;

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (TryFindIntProperty(doc.RootElement, "vocab_size", out int value) ||
                TryFindIntProperty(doc.RootElement, "vocabSize", out value))
                return value;
        }

        return null;
    }

    private static string? TryReadChatTemplate(string modelDir)
    {
        string path = Path.Combine(modelDir, "chat_template.jinja");
        if (!File.Exists(path))
            return null;

        return File.ReadAllText(path);
    }

    private static bool TryFindIntProperty(JsonElement element, string name, out int value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.Ordinal) &&
                    property.Value.TryGetInt32(out value))
                    return true;

                if (TryFindIntProperty(property.Value, name, out value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                if (TryFindIntProperty(item, name, out value))
                    return true;
        }

        value = 0;
        return false;
    }

    public void Dispose()
    {
        _tokenizer.Dispose();
        _model.Dispose();
        _config.Dispose();
        _ogaHandle.Dispose();
    }
}
