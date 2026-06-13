using Deckle.Input.Autocorrect.Injection;
using Deckle.Input.Autocorrect.Learning;
using Deckle.Input.Autocorrect.Lexicon;
using Deckle.Input.Autocorrect.Surfaces;
using Deckle.Input.Autocorrect.Tracking;
using Deckle.Input.Keyboard;

namespace Deckle.Input.Autocorrect.Engine;

// The conductor: raw key events → decode → track → decide → inject, with the
// revert gesture and the learning signals around it. Every handler runs on the
// KeyboardInputHost input thread; Start/Stop are the only cross-thread entries.
//
// Gate order is doctrine (CLAUDE.md): injected events are filtered first (our
// own repairs must never feed the tracker), the password gate cuts BEFORE
// decoding (keystrokes on a password surface are never even decoded), and the
// enrollment/editability gates withhold actions without stopping observation
// resets.
public sealed class AutocorrectEngine : IDisposable
{
    private const double RollupPeriodMs = 30_000;

    // Learning eligibility: a word already living in the base lexicons needs no
    // adoption; an English form above this frequency is "known English", not a
    // personal word. Calibration constant, not a setting.
    private const double EnglishKnownPerMillion = 0.5;

    private readonly IKeyboardInputHost _host;
    private readonly KeyDecoder _decoder;
    private readonly TypedWordTracker _tracker;
    private readonly ISurfaceProber _prober;
    private readonly ICorrectionPolicy _policy;
    private readonly ITextInjector _injector;
    private readonly PersonalDictionary? _dictionary;
    private readonly Func<AutocorrectSettings> _settings;
    private readonly FrequencyLexicon? _french;
    private readonly FrequencyLexicon? _english;

    private volatile FocusedSurface _surface = FocusedSurface.Unknown;

    // Armed after a correction lands; the very next physical keystroke either
    // triggers the revert (Backspace) or disarms it. The « come back later »
    // variant of the revert needs caret-position knowledge v1 does not have.
    private (string Original, string Replacement)? _revertArmed;

    // Rollup accumulators — input thread only.
    private double _rollupStartMs = -1;
    private int _rollupCommits;
    private int _rollupCorrections;
    private int _rollupReverts;
    private int _rollupLearning;
    private int _rollupGated;

    /// <summary>Raised on the input thread when the focused surface changes (surface, enrolled).</summary>
    public event Action<FocusedSurface, bool>? SurfaceChanged;

    /// <summary>Raised on the input thread after a correction burst landed.</summary>
    public event Action<CorrectionDecision>? CorrectionApplied;

    /// <summary>Raised on the input thread after a revert restored the literal (original, replacement).</summary>
    public event Action<string, string>? CorrectionReverted;

    /// <summary>
    /// Raised on the input thread when an injection burst did not land
    /// (original, replacement, isRevert) — UIPI-blocked elevated target,
    /// partial send. The screen may hold anything between the two forms.
    /// </summary>
    public event Action<string, string, bool>? InjectionFailed;

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
        FrequencyLexicon? french = null,
        FrequencyLexicon? english = null)
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
    }

    public bool Start()
    {
        _host.KeyReceived += OnKey;
        _host.PointerInteraction += OnPointerInteraction;
        _host.FocusChanged += OnFocusChanged;
        _tracker.WordCommitted += OnWordCommitted;
        _tracker.WordEdited += OnWordEdited;

        if (!_host.Start())
        {
            Unsubscribe();
            return false;
        }

        OnFocusChanged(); // seed the surface before the first focus event
        DeckleAutocorrectSource.Log.EngineStarted();
        return true;
    }

    public void Stop()
    {
        _host.Stop();
        Unsubscribe();
        _dictionary?.Flush();
        DeckleAutocorrectSource.Log.EngineStopped();
    }

    public void Dispose() => Stop();

    private void Unsubscribe()
    {
        _host.KeyReceived -= OnKey;
        _host.PointerInteraction -= OnPointerInteraction;
        _host.FocusChanged -= OnFocusChanged;
        _tracker.WordCommitted -= OnWordCommitted;
        _tracker.WordEdited -= OnWordEdited;
    }

    // ── Input thread handlers ────────────────────────────────────────────

    private void OnKey(KeyboardKeyEvent e)
    {
        if (e.IsInjected) return;            // our own repairs never feed the view
        if (_surface.IsPassword) return;     // hard gate — before decoding

        var stroke = _decoder.Decode(e);
        if (stroke is null) return;
        var k = stroke.Value;

        if (_revertArmed is { } armed)
        {
            _revertArmed = null;
            if (k.Kind == KeystrokeKind.Backspace)
            {
                HandleRevert(armed, k);
                return;
            }
        }

        _tracker.OnKeystroke(k);
    }

    // CONTEXT.md correction revert: the Backspace that deletes the boundary
    // sitting right after a corrected word also restores the original word.
    private void HandleRevert((string Original, string Replacement) armed, Keystroke backspace)
    {
        _tracker.OnKeystroke(backspace); // tracker re-opens the replacement

        if (_injector.Replace(armed.Replacement, armed.Original))
        {
            _tracker.ReplaceReopenedBuffer(armed.Original);
            _dictionary?.RecordRevert(
                armed.Original.ToLowerInvariant(), armed.Replacement.ToLowerInvariant());
            _rollupReverts++;
            DeckleAutocorrectSource.Log.CorrectionReverted();
            CorrectionReverted?.Invoke(armed.Original, armed.Replacement);
        }
        else
        {
            // The boundary is already gone (physical Backspace) and the word
            // still shows corrected: leave a trace, the tracker re-opened the
            // ORIGINAL and now disagrees with the screen.
            var plan = InjectionPlan.Compute(armed.Replacement, armed.Original);
            DeckleAutocorrectSource.Log.InjectionFailed(plan.Backspaces, plan.Text.Length);
            InjectionFailed?.Invoke(armed.Original, armed.Replacement, true);
        }
    }

    private void OnPointerInteraction()
    {
        _revertArmed = null;
        _tracker.NotifyPointerInteraction();
    }

    private void OnFocusChanged()
    {
        _revertArmed = null;
        var surface = _prober.Probe();
        _surface = surface;
        _tracker.NotifyFocusChanged();

        bool enrolled = IsEnrolled(_settings(), surface.ProcessName);
        DeckleAutocorrectSource.Log.SurfaceChanged(
            surface.ProcessName, surface.IsTextEditable, surface.IsPassword, enrolled);
        SurfaceChanged?.Invoke(surface, enrolled);
    }

    private void OnWordCommitted(WordCommit commit)
    {
        _rollupCommits++;

        var surface = _surface;
        var settings = _settings();
        bool actionable = settings.Enabled
                       && IsEnrolled(settings, surface.ProcessName)
                       && surface.IsTextEditable
                       && !surface.IsPassword;
        if (!actionable)
        {
            _rollupGated++;
            MaybeRollup(commit.TimestampMs);
            return;
        }

        var decision = _policy.Evaluate(commit.Word, commit.PreviousWord);

        // A reverted pair stays suppressed whatever the policy says — enforced
        // here so even a policy without dictionary access (the CLI toy) honors
        // the gesture.
        if (decision is not null
            && _dictionary?.IsSuppressed(decision.Original, decision.Replacement) == true)
            decision = null;

        // Learning feeds on words the engine leaves alone. A corrected commit
        // must NOT reinforce the bare typo, or a few repetitions would adopt it
        // and silently disable its own correction.
        if (decision is null)
            RecordCommitLearning(commit.Word);
        else
            ApplyCorrection(commit, decision);

        MaybeRollup(commit.TimestampMs);
    }

    private void ApplyCorrection(WordCommit commit, CorrectionDecision decision)
    {
        string boundary = commit.Boundary.ToString();
        string current = decision.Original + boundary;
        string target = decision.Replacement + boundary;
        var plan = InjectionPlan.Compute(current, target);

        if (_injector.Replace(current, target))
        {
            _tracker.ReplaceLastCommitted(decision.Replacement);
            _revertArmed = (decision.Original, decision.Replacement);
            _rollupCorrections++;
            DeckleAutocorrectSource.Log.CorrectionApplied();
            DeckleAutocorrectSource.Log.CorrectionDetail(
                decision.Reason.ToString(), decision.Original.Length,
                decision.Replacement.Length, plan.Backspaces);
            CorrectionApplied?.Invoke(decision);
        }
        else
        {
            DeckleAutocorrectSource.Log.InjectionFailed(plan.Backspaces, plan.Text.Length);
            InjectionFailed?.Invoke(decision.Original, decision.Replacement, false);
        }
    }

    private void OnWordEdited(WordEdit edit)
    {
        var surface = _surface;
        var settings = _settings();
        if (!settings.Enabled || !IsEnrolled(settings, surface.ProcessName)
            || !surface.IsTextEditable || surface.IsPassword)
            return;

        // « typed bare, went back, fixed the accents by hand » — the strongest
        // organic signal that the accented form is the wanted one.
        string o = edit.Original, r = edit.Replacement;
        if (!string.Equals(o, r, StringComparison.Ordinal)
            && AccentFolding.Fold(o) == AccentFolding.Fold(r)
            && AccentFolding.HasDiacritics(r)
            && !AccentFolding.HasDiacritics(o))
        {
            _dictionary?.RecordManualAccentFix(o.ToLowerInvariant(), r.ToLowerInvariant());
            _rollupLearning++;
            DeckleAutocorrectSource.Log.LearningSignal("manual_accent_fix");
        }
    }

    // Commits feed adoption only for plain out-of-lexicon words — the base
    // lexicons need no reinforcement, and content guards back the password
    // gate (no digits, sane length).
    private void RecordCommitLearning(string word)
    {
        if (_dictionary is null) return;
        if (word.Length < 2 || word.Length > 30) return;
        if (word.EndsWith('\'')) return;
        foreach (char c in word)
            if (!char.IsLetter(c) && c != '-' && c != '\'') return;

        string lower = word.ToLowerInvariant();
        if (_french?.Contains(lower) == true) return;
        if (_english?.FrequencyOf(lower) >= EnglishKnownPerMillion) return;

        _dictionary.RecordCommit(lower);
        _rollupLearning++;
        DeckleAutocorrectSource.Log.LearningSignal("commit");
    }

    private static bool IsEnrolled(AutocorrectSettings settings, string processName)
    {
        if (processName.Length == 0) return false;
        foreach (string enrolled in settings.EnrolledProcesses)
            if (string.Equals(enrolled, processName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private void MaybeRollup(double nowMs)
    {
        if (_rollupStartMs < 0) _rollupStartMs = nowMs;
        if (nowMs - _rollupStartMs < RollupPeriodMs) return;

        DeckleAutocorrectSource.Log.ActivityRollup(
            _rollupCommits, _rollupCorrections, _rollupReverts, _rollupLearning, _rollupGated);

        _rollupStartMs = nowMs;
        _rollupCommits = 0;
        _rollupCorrections = 0;
        _rollupReverts = 0;
        _rollupLearning = 0;
        _rollupGated = 0;
    }
}
