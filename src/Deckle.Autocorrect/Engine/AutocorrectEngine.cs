using Deckle.Autocorrect;
using Deckle.Diagnostics;
using Deckle.Input;
using System.Diagnostics.Tracing;
using System.Text;

namespace Deckle.Autocorrect;

// The conductor: raw key events → decode → track → decide → inject, with the
// learning signals around it. Every handler runs on the KeyboardInputHost
// input thread; Start/Stop are the only cross-thread entries.
//
// A correction is taken back only through the correction inlay (CONTEXT.md
// § Correction undo) — Backspace is always a plain Backspace. The earlier
// implicit-Backspace revert was retired after its misfires (a deleted comma
// read as an undo) wrote false suppressions (JOURNAL 2026-07-02).
//
// Gate order is doctrine (CLAUDE.md): injected events are filtered first (our
// own repairs must never feed the tracker), the password gate cuts BEFORE
// decoding (keystrokes on a password surface are never even decoded), and the
// enrollment/editability gates withhold actions without stopping observation
// resets.
public sealed partial class AutocorrectEngine : IDisposable
{
    private const double RollupPeriodMs = 30_000;

    private readonly IKeyboardInputHost _host;
    private readonly KeyDecoder _decoder;
    private readonly TypedWordTracker _tracker;
    private readonly ISurfaceProber _prober;
    private readonly ICorrectionPolicy _policy;
    private readonly ITextInjector _injector;
    private readonly PersonalDictionary? _dictionary;
    private readonly Func<AutocorrectSettings> _settings;
    private readonly IFrequencyLexicon? _french;
    private readonly IFrequencyLexicon? _english;

    // Start/Stop own one reference on the process-shared input host. Keeping
    // that ownership explicit is essential: Dispose before Start must not
    // release a reference held by another consumer (for example wheel capture).
    private readonly object _lifecycleLock = new();
    private bool _started;
    private bool _disposed;

    // The approved mistouch families' detector-generator (CONTEXT.md § Mistouch
    // family) — null when no family is approved, and the engine runs untouched.
    // Kinds are code, the records are the user's own data.
    private readonly MistouchFamilyCorrector? _mistouch;

    // Opt-in per-word decision telemetry. When this returns true, each evaluated
    // word on an enrolled surface emits a structured trace (candidates, scores,
    // margins, the guard that left it literal) to the autocorrect.decisions dataset.
    // Null/false = the chain runs untouched, no trace allocated. Off by default.
    private readonly Func<bool>? _decisionTelemetry;

    // Opt-in typed-sentence corpus. When this returns true, each committed word on
    // an enrolled, editable, non-password surface feeds the accumulator. The same
    // surface gate as correction keeps verbatim collection inside the scope the user
    // explicitly approved. The accumulator emits the
    // (typed, final) sentence pair to the autocorrect.text dataset. Null = no
    // accumulator built. Off by default. The heaviest text capture — a verbatim
    // record of typed input, so it stands behind its own dedicated consent toggle.
    private readonly Func<bool>? _textTelemetry;
    private readonly SentenceCorpus? _corpus;
    private int _discardCorpusRequested;

    // The typing stream (CONTEXT.md § Typing stream) rides the same consent
    // envelope and the same enrolled-surface gate as the corpus. Fed the same decoded keystrokes
    // the tracker consumes — a parallel verbatim capture, not a word model.
    private readonly TypingStream? _stream;

    // The post-sentence contextual stage. Null only when no sentence reranker is
    // wired; the reranker may be deterministic rules alone or rules delegating to
    // an ONNX model. The lane owns the background inference thread; the
    // coordinator owns the sentence model.
    private readonly SentenceRerankCoordinator? _coordinator;
    private readonly IRerankLane? _lane;

    private volatile FocusedSurface _surface = FocusedSurface.Unknown;

    // Apps already offered for enrollment this run — a would-be correction on an
    // undecided surface prompts once, then stays silent until the user answers.
    private readonly HashSet<string> _suggested = new(StringComparer.OrdinalIgnoreCase);

    // Monotonic per-word id, input thread only. Stamps each evaluated word so its
    // synchronous decision line and the deferred reranker verdict join on one id.
    private long _wordId;

    // One coalesced foreign SendInput burst. Only Backspace + VK_PACKET text is
    // eligible for reconciliation; every other shape falls back to a hard reset.
    private bool _foreignMutationOpen;
    private string? _foreignMutationFallback;
    private int _foreignBackspaces;
    private StringBuilder? _foreignReplacement;

    // Rollup accumulators — input thread only.
    private double _rollupStartMs = -1;
    private int _rollupCommits;
    private int _rollupCorrections;
    // Words the user reopened and retyped after they committed — the personal
    // Words-Modified-Ratio numerator (WMR = re_edited / commits), the fate signal
    // that a committed word (corrected or not) did not stand.
    private int _rollupReEdited;
    private int _rollupLearning;
    private int _rollupGated;
    private bool _injectionIncidentOpen;
    private int _injectionFailures;
    private int _lastInjectionBackspaces;
    private int _lastInjectionTextLength;

    /// <summary>Raised on the input thread when the focused surface changes (surface, enrolled).</summary>
    public event Action<FocusedSurface, bool>? SurfaceChanged;

    /// <summary>Raised on the input thread after a correction burst landed.</summary>
    public event Action<CorrectionDecision>? CorrectionApplied;

    /// <summary>
    /// Raised on the input thread when an injection burst did not land
    /// (original, replacement) — UIPI-blocked elevated target, partial send.
    /// The screen may hold anything between the two forms.
    /// </summary>
    public event Action<string, string>? InjectionFailed;

    /// <summary>
    /// Raised on the input thread when a correction would have applied on an
    /// editable, non-password, not-yet-decided surface — the signal to offer
    /// enrollment for that process. Fires at most once per process per run.
    /// </summary>
    public event Action<string>? EnrollmentSuggested;

    public FocusedSurface CurrentSurface => _surface;

    public AutocorrectEngine(
        IKeyboardInputHost host,
        KeyDecoder decoder,
        TypedWordTracker tracker,
        ISurfaceProber prober,
        ICorrectionPolicy policy,
        ITextInjector injector,
        Func<AutocorrectSettings> settings,
        PersonalDictionary? dictionary = null,
        IFrequencyLexicon? french = null,
        IFrequencyLexicon? english = null,
        ISentenceReranker? reranker = null,
        IAmbiguityProbe? probe = null,
        IAmbiguityProbe? wholeSentenceProbe = null,
        Func<bool>? decisionTelemetry = null,
        Func<bool>? textTelemetry = null,
        IReadOnlyList<MistouchFamilyRecord>? mistouchFamilies = null,
        ICaretTextReader? caretTextReader = null,
        IRerankLane? rerankLaneOverride = null)
    {
        _host = host;
        _decoder = decoder;
        _tracker = tracker;
        _prober = prober;
        _policy = policy;
        _injector = injector;
        _settings = settings;
        _dictionary = dictionary;
        _french = french;
        _english = english;
        _decisionTelemetry = decisionTelemetry;
        _textTelemetry = textTelemetry;
        _caretTextReader = caretTextReader;
        _corpus = textTelemetry is null ? null : new SentenceCorpus { Completed = EmitText };
        _stream = textTelemetry is null ? null : new TypingStream { Completed = EmitStreamRun };
        _mistouch = mistouchFamilies is { Count: > 0 }
            ? new MistouchFamilyCorrector(mistouchFamilies, IsProtectedWord)
            : null;

        if (reranker is not null && rerankLaneOverride is not null)
            throw new ArgumentException(
                "A reranker lane override cannot be combined with a reranker.",
                nameof(rerankLaneOverride));

        // The contextual stage exists only with both a model and a probe. The lane
        // marshals inference off this thread and the verdict back via the host pump.
        if (probe is not null && (reranker is not null || rerankLaneOverride is not null))
        {
            // Production never supplies an override and retains the background
            // lane. The internal benchmark seam can inject a synchronous lane to
            // prove request absence without converting a scheduling timeout into
            // a generator-coverage result.
            _lane = rerankLaneOverride
                ?? new BackgroundRerankLane(reranker!, host, caretTextReader);
            _coordinator = new SentenceRerankCoordinator(
                lane: _lane,
                probe: probe,
                injector: injector,
                currentPartial: () => tracker.CurrentWord,
                realignLastCommitted: tracker.ReplaceLastCommitted,
                onApplied: OnCoordinatorApplied,
                decisionTelemetry: decisionTelemetry,
                wholeSentenceProbe: wholeSentenceProbe);
        }
    }
}
