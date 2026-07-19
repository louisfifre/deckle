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

            (config, model) = CreateModel(modelDir, _executionProvider);
            tokenizer = new Tokenizer(model);

            _ogaHandle = ogaHandle;
            _config = config;
            _model = model;
            _tokenizer = tokenizer;
            _bosTokenId = TryGetBosTokenId(_tokenizer);
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
    // chosen in code, not read from the export's genai_config.json, so one CPU
    // int4 export can be driven onto the GPU (DirectML) without a re-export:
    // clear the config's providers and append the chosen one. "cpu" leaves the
    // list empty → the built-in CPU EP. Model construction enumerates the DML
    // devices, and that enumeration fails transiently — measured on the test
    // host (2026-07-14): "Specified provider is not supported" on one run,
    // clean on the next, same binary and machine. One retry absorbs the flake
    // for every consumer (live composition, probe, replay); a second failure
    // is a real one and propagates. The config is rebuilt per attempt rather
    // than reused across a failed native construction.
    private static (Config Config, Model Model) CreateModel(string modelDir, string executionProvider)
    {
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
            catch (OnnxRuntimeGenAIException) when (attempt == 0)
            {
                config?.Dispose();
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
    {
        try
        {
            if (!Directory.Exists(modelDir))
                return null;

            return new OnnxSentenceScorer(modelDir, margin, executionProvider);
        }
        catch
        {
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
        catch
        {
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
