using Deckle.Autocorrect;
using Deckle.Diagnostics;
using Deckle.Input;
using System.Diagnostics.Tracing;
using System.Text;

namespace Deckle.Autocorrect;

// The conductor: raw key events → decode → track → decide → inject, with the
// learning signals around it. Every handler runs on the KeyboardInputHost
// input thread; Start, Stop and Dispose are the only cross-thread entries.
//
// Backspace is always a plain Backspace, never a correction undo: reading it
// as an undo misfires (a deleted comma read as an undo) and writes false
// suppressions. Undo belongs to the correction inlay.
//
// Gate order is doctrine: injected events are filtered first (our
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

    // What counts as the person's own word when a corrector asks: the
    // dictionary's adopted words, plus whatever the composition root adds
    // (the declared lexicon). The policies get the same view, so no corrector
    // can correct away a word another one protects.
    private readonly IPersonalLexicon? _adopted;
    private readonly Func<AutocorrectSettings> _settings;
    private readonly IFrequencyLexicon? _french;
    private readonly IFrequencyLexicon? _english;

    // Start/Stop own one reference on the process-shared input host. Keeping
    // that ownership explicit is essential: Dispose before Start must not
    // release a reference held by another consumer (for example wheel capture).
    private readonly object _lifecycleLock = new();
    private bool _started;
    private bool _disposed;

    // Mistouch kinds are code; the family records are the user's own data.
    private readonly MistouchFamilyCorrector? _mistouch;

    private volatile FocusedSurface _surface = FocusedSurface.Unknown;

    // Apps already offered for enrollment this run — a would-be correction on an
    // undecided surface prompts once, then stays silent until the user answers.
    private readonly HashSet<string> _suggested = new(StringComparer.OrdinalIgnoreCase);

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
        IReadOnlyList<MistouchFamilyRecord>? mistouchFamilies = null,
        IPersonalLexicon? adopted = null)
    {
        _host = host;
        _decoder = decoder;
        _tracker = tracker;
        _prober = prober;
        _policy = policy;
        _injector = injector;
        _settings = settings;
        _dictionary = dictionary;
        _adopted = adopted ?? dictionary;
        _french = french;
        _english = english;
        _mistouch = mistouchFamilies is { Count: > 0 }
            ? new MistouchFamilyCorrector(mistouchFamilies, IsProtectedWord)
            : null;
    }
}
