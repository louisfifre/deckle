using System.Diagnostics;
using System.Text;
using Deckle.Input;

namespace Deckle.Autocorrect.Probe;

// Headless end-of-field keyboard session. Only the three OS ports are
// substituted; decoding, tracking, policy ordering, injection planning and
// sentence-slot preparation are production code. Cost sampling surrounds the
// boundary key's synchronous engine handler, after the surface received the
// physical character and before any benchmark bookkeeping.
internal sealed class BenchmarkKeyboardSession : IDisposable
{
    private readonly BenchmarkKeyboardHost _host = new();
    private readonly BenchmarkSurfaceProber _prober = new();
    private readonly BenchmarkTextSurface _surface = new();
    private readonly BenchmarkTextInjector _injector;
    private readonly TypedWordTracker _tracker = new();
    private readonly Dictionary<ushort, char> _layout = new();
    private readonly KeyDecoder _decoder;
    private readonly AutocorrectEngine _engine;
    private readonly Action<CommitCostSample>? _costSink;
    private readonly CandidateCommitCollector? _candidateCollector;
    private double _timestampMs;

    public BenchmarkKeyboardSession(
        ICorrectionPolicy policy,
        IFrequencyLexicon french,
        IFrequencyLexicon english,
        IAmbiguityProbe? probe = null,
        IAmbiguityProbe? wholeSentenceProbe = null,
        ISentenceReranker? reranker = null,
        IRerankLane? rerankLaneOverride = null,
        bool recordCorrections = false,
        Action<CommitCostSample>? costSink = null,
        CandidateCommitCollector? candidateCollector = null)
    {
        _injector = new BenchmarkTextInjector(_surface);
        _costSink = costSink;
        _candidateCollector = candidateCollector;
        _decoder = new KeyDecoder((vk, _, _, buffer) =>
        {
            buffer.Clear();
            if (!_layout.TryGetValue(vk, out char value))
                return 0;
            buffer.Append(value);
            return 1;
        });

        var settings = new AutocorrectSettings();
        settings.Apps["codex"] = true;
        _prober.Surface = new FocusedSurface(
            "codex", IsPassword: false, IsTextEditable: true);

        _engine = new AutocorrectEngine(
            _host,
            _decoder,
            _tracker,
            _prober,
            policy,
            _injector,
            () => settings,
            french: french,
            english: english,
            reranker: reranker,
            probe: probe,
            wholeSentenceProbe: wholeSentenceProbe,
            rerankLaneOverride: rerankLaneOverride);

        if (recordCorrections)
            _engine.CorrectionApplied += Applied.Add;
        _engine.InjectionFailed += (_, _) => InjectionFailureCount++;

        if (!_engine.Start())
            throw new InvalidOperationException("The benchmark keyboard engine did not start.");
    }

    public List<CorrectionDecision> Applied { get; } = new();

    public int InjectionFailureCount { get; private set; }

    public string VisibleText => _surface.Text;

    public void BeginScenario(bool startsAfterObservedEnter = false)
    {
        _surface.Clear();
        Applied.Clear();
        _timestampMs = 0.0;
        _host.RaiseFocusChanged();
        if (startsAfterObservedEnter)
        {
            _host.RaiseKey(new KeyboardKeyEvent(
                VirtualKey: 0x0D,
                ScanCode: 0,
                IsKeyDown: true,
                IsExtended: false,
                IsInjected: false,
                TimestampMs: _timestampMs));
        }
    }

    public void Type(string text, int interKeyMs = 35)
    {
        foreach (char value in text)
        {
            string currentWord = _tracker.CurrentWord;
            bool commitsWord = WillCommit(currentWord, value);
            ushort virtualKey = VirtualKeyFor(value);
            _layout[virtualKey] = value;
            _surface.Type(value);

            if (!commitsWord)
            {
                _host.RaiseKey(new KeyboardKeyEvent(
                    virtualKey, ScanCode: 0, IsKeyDown: true,
                    IsExtended: false, IsInjected: false,
                    TimestampMs: _timestampMs));
                _timestampMs += interKeyMs;
                continue;
            }

            _candidateCollector?.Begin(currentWord);
            long allocatedBefore = _costSink is null
                ? 0L
                : GC.GetAllocatedBytesForCurrentThread();
            long started = _costSink is null ? 0L : Stopwatch.GetTimestamp();

            _host.RaiseKey(new KeyboardKeyEvent(
                virtualKey, ScanCode: 0, IsKeyDown: true,
                IsExtended: false, IsInjected: false,
                TimestampMs: _timestampMs));

            if (_costSink is not null)
            {
                long elapsed = Stopwatch.GetTimestamp() - started;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                _costSink(new CommitCostSample(currentWord, elapsed, allocated));
            }
            _candidateCollector?.End();
            _timestampMs += interKeyMs;
        }
    }

    public void Backspace()
    {
        _surface.Backspace();
        _host.RaiseKey(new KeyboardKeyEvent(
            VirtualKey: 0x08,
            ScanCode: 0,
            IsKeyDown: true,
            IsExtended: false,
            IsInjected: false,
            TimestampMs: _timestampMs));
    }

    public void Dispose() => _engine.Dispose();

    private static bool WillCommit(string currentWord, char value)
    {
        if (currentWord.Length == 0 || WordBoundaries.IsWordChar(value))
            return false;
        return !WordBoundaries.IsApostrophe(value)
            || WordBoundaries.IsElisionPrefix(currentWord);
    }

    private static ushort VirtualKeyFor(char value) => value switch
    {
        ' ' => 0x20,
        >= 'a' and <= 'z' => (ushort)('A' + value - 'a'),
        >= 'A' and <= 'Z' => value,
        >= '0' and <= '9' => value,
        '\'' => 0xDE,
        '-' => 0xBD,
        '.' or '…' => 0xBE,
        ',' => 0xBC,
        ';' or ':' => 0xBA,
        '?' => 0xBF,
        '!' => 0x31,
        '"' => 0xDE,
        '(' => 0x39,
        ')' => 0x30,
        _ => 0xE2,
    };
}

internal readonly record struct CommitCostSample(
    string Word,
    long ElapsedTicks,
    long AllocatedBytes)
{
    public double LatencyMicroseconds =>
        ElapsedTicks * (1_000_000.0 / Stopwatch.Frequency);
}

internal sealed class CandidateCommitCollector
{
    private readonly List<CandidateCommitSample> _samples = new();
    private string? _word;
    private int _commitGenerated;
    private int _sentenceGenerated;
    private int _distinctLookups;
    private int _matches;

    public IReadOnlyList<CandidateCommitSample> Samples => _samples;

    public void Begin(string word)
    {
        _word = word;
        _commitGenerated = 0;
        _sentenceGenerated = 0;
        _distinctLookups = 0;
        _matches = 0;
    }

    public void Observe(CandidateSearchObservation observation)
    {
        if (_word is null)
            return;
        if (observation.Path == CandidateSearchPath.Commit)
            _commitGenerated += observation.Generated;
        else
            _sentenceGenerated += observation.Generated;
        _distinctLookups += observation.DistinctLookups;
        _matches += observation.Matches;
    }

    public void End()
    {
        if (_word is null)
            return;
        _samples.Add(new CandidateCommitSample(
            _word,
            _commitGenerated,
            _sentenceGenerated,
            _distinctLookups,
            _matches));
        _word = null;
    }
}

internal readonly record struct CandidateCommitSample(
    string Word,
    int CommitGenerated,
    int SentenceGenerated,
    int DistinctLookups,
    int Matches)
{
    public int Generated => CommitGenerated + SentenceGenerated;
}

internal sealed class BenchmarkKeyboardHost : IKeyboardInputHost
{
    public event Action<KeyboardKeyEvent>? KeyReceived;
    public event Action? FocusChanged;

    public event Action? PointerInteraction
    {
        add { }
        remove { }
    }

    public event Action<MouseWheelEvent>? WheelObserved
    {
        add { }
        remove { }
    }

    public event Action? DrainRequested
    {
        add { }
        remove { }
    }

    public bool Start() => true;
    public void Stop() { }
    public void SetWheelInterceptor(IWheelInterceptor? interceptor) { }

    public void RequestDrain() { }

    public void RaiseKey(KeyboardKeyEvent value) => KeyReceived?.Invoke(value);
    public void RaiseFocusChanged() => FocusChanged?.Invoke();
}

internal sealed class BenchmarkSurfaceProber : ISurfaceProber
{
    public FocusedSurface Surface { get; set; } = FocusedSurface.Unknown;
    public FocusedSurface Probe() => Surface;
}

internal sealed class BenchmarkTextInjector(BenchmarkTextSurface surface) : ITextInjector
{
    public bool Replace(string current, string target) =>
        surface.ReplaceSuffix(current, target);
}

internal sealed class BenchmarkTextSurface
{
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public void Clear() => _text.Clear();

    public void Type(char value) => _text.Append(value);

    public void Backspace()
    {
        if (_text.Length > 0)
            _text.Length--;
    }

    public bool ReplaceSuffix(string current, string target)
    {
        if (_text.Length < current.Length)
            return false;
        int start = _text.Length - current.Length;
        for (int index = 0; index < current.Length; index++)
            if (_text[start + index] != current[index])
                return false;
        _text.Length = start;
        _text.Append(target);
        return true;
    }
}
