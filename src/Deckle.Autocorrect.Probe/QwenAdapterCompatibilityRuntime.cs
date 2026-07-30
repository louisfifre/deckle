using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Autocorrect.Probe;

internal sealed class OnnxQwenAdapterProbeRuntimeFactory : IQwenAdapterProbeRuntimeFactory
{
    public IQwenAdapterProbeRuntime Create(QwenAdapterCompatibilityPlan plan) => new Runtime(plan);

    private sealed class Runtime : IQwenAdapterProbeRuntime
    {
        private readonly OgaHandle _ogaHandle;
        private readonly Config _config;
        private readonly Model _model;
        private readonly Tokenizer _tokenizer;
        private readonly Adapters _adapters;
        private readonly HashSet<string> _loadedNames = new(StringComparer.Ordinal);
        private readonly List<string> _cleanupFailures = [];
        private bool _disposed;

        public Runtime(QwenAdapterCompatibilityPlan plan)
        {
            long started = Stopwatch.GetTimestamp();
            OgaHandle? ogaHandle = null;
            Config? config = null;
            Model? model = null;
            Tokenizer? tokenizer = null;
            Adapters? adapters = null;
            try
            {
                ogaHandle = new OgaHandle();
                config = new Config(plan.ModelDirectory);
                config.ClearProviders();
                model = new Model(config);
                tokenizer = new Tokenizer(model);
                adapters = new Adapters(model);

                _ogaHandle = ogaHandle;
                _config = config;
                _model = model;
                _tokenizer = tokenizer;
                _adapters = adapters;
                ModelLoadMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            catch (Exception exception)
            {
                var cleanupFailures = new List<string>();
                TryCleanup(adapters, "adapters_dispose", cleanupFailures);
                TryCleanup(tokenizer, "tokenizer_dispose", cleanupFailures);
                TryCleanup(model, "model_dispose", cleanupFailures);
                TryCleanup(config, "config_dispose", cleanupFailures);
                TryCleanup(ogaHandle, "oga_dispose", cleanupFailures);
                throw new QwenAdapterRuntimeCreationException(
                    exception,
                    cleanupFailures.AsReadOnly());
            }
        }

        public int ModelInstanceCount => 1;
        public double ModelLoadMilliseconds { get; }
        public IReadOnlyList<string> CleanupFailures => _cleanupFailures.AsReadOnly();

        public IReadOnlyList<int> Encode(string text)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            using Sequences sequences = _tokenizer.Encode(text);
            return sequences[0].ToArray();
        }

        public void LoadAdapter(string path, string name)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _adapters.LoadAdapter(path, name);
            _loadedNames.Add(name);
        }

        public IQwenAdapterProbeRequest CreateRequest(IReadOnlyList<int> tokens)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(tokens);
            if (tokens.Count == 0)
                throw new InvalidDataException("The frozen input has no token.");
            int[] inputTokens = tokens.ToArray();

            long started = Stopwatch.GetTimestamp();
            var generatorParams = new GeneratorParams(_model);
            Generator? generator = null;
            try
            {
                generatorParams.SetSearchOption("max_length", inputTokens.Length + 1);
                generator = new Generator(_model, generatorParams);
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                return new Request(
                    generatorParams,
                    generator,
                    _adapters,
                    inputTokens,
                    elapsed,
                    _cleanupFailures);
            }
            catch
            {
                TryCleanup(
                    generator,
                    "request_generator_create_cleanup",
                    _cleanupFailures);
                TryCleanup(
                    generatorParams,
                    "request_params_create_cleanup",
                    _cleanupFailures);
                throw;
            }
        }

        public void UnloadAdapter(string name)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _adapters.UnloadAdapter(name);
            _loadedNames.Remove(name);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (string name in _loadedNames.ToArray())
            {
                TryCleanup(
                    () => _adapters.UnloadAdapter(name),
                    $"unload:{name}");
                _loadedNames.Remove(name);
            }
            TryCleanup(_adapters.Dispose, "adapters_dispose");
            TryCleanup(_tokenizer.Dispose, "tokenizer_dispose");
            TryCleanup(_model.Dispose, "model_dispose");
            TryCleanup(_config.Dispose, "config_dispose");
            TryCleanup(_ogaHandle.Dispose, "oga_dispose");
        }

        private void TryCleanup(Action cleanup, string stage)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                _cleanupFailures.Add($"{stage}:{exception.GetType().Name}");
            }
        }

        private static void TryCleanup(
            IDisposable? disposable,
            string stage,
            ICollection<string> cleanupFailures)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (Exception exception)
            {
                cleanupFailures.Add($"{stage}:{exception.GetType().Name}");
            }
        }
    }

    private sealed class Request : IQwenAdapterProbeRequest
    {
        private readonly GeneratorParams _generatorParams;
        private readonly Generator _generator;
        private readonly Adapters _adapters;
        private readonly int[] _tokens;
        private readonly double _generatorCreateMilliseconds;
        private readonly ICollection<string> _cleanupFailures;
        private bool _adapterSelected;
        private bool _executed;
        private bool _disposed;
        private double _activationMilliseconds;

        public Request(
            GeneratorParams generatorParams,
            Generator generator,
            Adapters adapters,
            int[] tokens,
            double generatorCreateMilliseconds,
            ICollection<string> cleanupFailures)
        {
            _generatorParams = generatorParams;
            _generator = generator;
            _adapters = adapters;
            _tokens = tokens;
            _generatorCreateMilliseconds = generatorCreateMilliseconds;
            _cleanupFailures = cleanupFailures;
        }

        public double GeneratorCreateMilliseconds => _generatorCreateMilliseconds;

        public double SetActiveAdapter(string name)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (_executed || _adapterSelected)
                throw new InvalidOperationException(
                    "An adapter must be selected exactly once before the first forward.");

            long started = Stopwatch.GetTimestamp();
            _generator.SetActiveAdapter(_adapters, name);
            _activationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _adapterSelected = true;
            return _activationMilliseconds;
        }

        public QwenAdapterRuntimeObservation Execute(
            string state,
            int ordinal,
            bool retainComparisonValues = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_executed)
                throw new InvalidOperationException("A probe request can execute only once.");
            _executed = true;

            long started = Stopwatch.GetTimestamp();
            _generator.AppendTokens(_tokens);
            using Tensor logits = _generator.GetOutput("logits");
            ElementType elementType = logits.Type();
            long[] shape = logits.Shape();
            if (elementType != ElementType.float16)
            {
                throw new InvalidDataException(
                    $"Expected float16 logits, observed {elementType}.");
            }

            ReadOnlySpan<Half> data = logits.GetData<Half>();
            byte[] fingerprint = SHA256.HashData(MemoryMarshal.AsBytes(data));
            bool finite = true;
            for (int i = 0; i < data.Length; i++)
                finite &= Half.IsFinite(data[i]);
            Half[]? comparisonValues = retainComparisonValues
                ? data.ToArray()
                : null;

            double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return new QwenAdapterRuntimeObservation(
                state,
                ordinal,
                Convert.ToHexStringLower(fingerprint),
                shape,
                "float16",
                finite,
                _generatorCreateMilliseconds,
                _activationMilliseconds,
                elapsed,
                data.Length,
                comparisonValues);
        }

        public QwenForcedCandidateScore ScoreCandidate(
            string id,
            int promptTokenCount,
            IReadOnlyList<int> completionTokenIds,
            int scoreStartInclusive,
            int scoreEndExclusive)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(completionTokenIds);
            if (_executed)
                throw new InvalidOperationException("A probe request can execute only once.");
            if (promptTokenCount <= 0
                || scoreStartInclusive < 0
                || scoreEndExclusive <= scoreStartInclusive
                || scoreEndExclusive > completionTokenIds.Count
                || _tokens.Length != promptTokenCount + scoreEndExclusive)
                throw new InvalidDataException("The frozen forced-score span is invalid.");
            _executed = true;

            _generator.AppendTokens(_tokens);
            using Tensor logits = _generator.GetOutput("logits");
            if (logits.Type() != ElementType.float16)
                throw new InvalidDataException("Forced-score logits are not float16.");
            long[] shape = logits.Shape();
            if (shape.Length != 3
                || shape[0] != 1
                || shape[1] != _tokens.Length
                || shape[2] <= 0
                || shape[2] > int.MaxValue
                || logits.NumElements() != checked(shape[0] * shape[1] * shape[2]))
                throw new InvalidDataException("Forced-score logits have invalid geometry.");

            int vocabularySize = checked((int)shape[2]);
            ReadOnlySpan<Half> values = logits.GetData<Half>();
            bool finite = true;
            for (int index = 0; index < values.Length; index++)
                finite &= Half.IsFinite(values[index]);

            double logProbability = 0.0;
            var row = new float[vocabularySize];
            for (int next = scoreStartInclusive; next < scoreEndExclusive; next++)
            {
                int tokenId = completionTokenIds[next];
                int predictPosition = promptTokenCount + next - 1;
                if (tokenId < 0
                    || tokenId >= vocabularySize
                    || predictPosition < 0
                    || predictPosition >= shape[1])
                    throw new InvalidDataException("Forced-score token is outside logits.");

                int rowStart = checked(predictPosition * vocabularySize);
                for (int column = 0; column < vocabularySize; column++)
                    row[column] = (float)values[rowStart + column];
                logProbability += QwenCandidateScoringMath.LogProbability(row, tokenId);
            }

            int scored = scoreEndExclusive - scoreStartInclusive;
            double score = logProbability / scored;
            return new QwenForcedCandidateScore(
                id,
                score,
                logProbability,
                scored,
                finite && double.IsFinite(score) && double.IsFinite(logProbability),
                shape);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            TryDispose(_generator, "request_generator_dispose");
            TryDispose(_generatorParams, "request_params_dispose");
        }

        private void TryDispose(IDisposable disposable, string stage)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                _cleanupFailures.Add($"{stage}:{exception.GetType().Name}");
            }
        }
    }
}

internal sealed class QwenAdapterRuntimeCreationException : Exception
{
    public QwenAdapterRuntimeCreationException(
        Exception innerException,
        IReadOnlyList<string> cleanupFailures)
        : base("The ACX-0023 runtime could not be constructed.", innerException)
    {
        OriginalExceptionType = innerException.GetType().Name;
        CleanupFailures = cleanupFailures;
    }

    public string OriginalExceptionType { get; }
    public IReadOnlyList<string> CleanupFailures { get; }
}

internal static class QwenCandidateScoringMath
{
    public static double LogProbability(ReadOnlySpan<float> logits, int tokenId)
    {
        if ((uint)tokenId >= (uint)logits.Length || logits.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(tokenId));

        double maximum = double.NegativeInfinity;
        for (int index = 0; index < logits.Length; index++)
        {
            double value = logits[index];
            if (!double.IsFinite(value))
                throw new InvalidDataException("A forced-score row contains non-finite logits.");
            maximum = Math.Max(maximum, value);
        }

        double sum = 0.0;
        for (int index = 0; index < logits.Length; index++)
            sum += Math.Exp(logits[index] - maximum);
        return logits[tokenId] - maximum - Math.Log(sum);
    }
}
