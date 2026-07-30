using System.Diagnostics;
using System.Text.Json;
using Deckle.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Playground;

// Physical-typing lab for ACX-0021 phase A. It owns both the editor and the
// delayed range write; it does not call production autocorrect, UI Automation,
// SendInput, or any cross-process mutation API.
public sealed partial class CorrectionApplicationPage : Page
{
    private readonly RichEditCorrectionSurface _surface;
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _delayClock = new();
    private readonly Stopwatch _releaseClock = new();
    private readonly List<CorrectionApplicationAttempt> _attempts = [];
    private CorrectionApplicationTrial? _trial;
    private CorrectionApplicationReason? _releaseAuthorityFailure;
    private CorrectionApplicationFixture _fixture = CorrectionApplicationFixtures.All[0];
    private CorrectionSurfaceExecution _executionEvidence;
    private CorrectionSurfaceFault _fault;
    private CorrectionSurfaceSnapshot? _releaseSnapshot;
    private CorrectionApplicationSelection? _postSelection;
    private AttemptGateEvidence _gateEvidence;
    private CompositionAuthority _composition = CompositionAuthority.Unknown;
    private long _readOnlyCallbackToken;
    private int _requestedDelayMs;
    private long? _actualReleaseDelayMs;
    private long _attemptIndex;
    private bool _isLoaded;
    private bool _isDisposed;
    private bool _suppressObservation;

    public CorrectionApplicationPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        _surface = new RichEditCorrectionSurface(Editor);
        FixtureComboBox.ItemsSource = CorrectionApplicationFixtures.All;
        FixtureComboBox.SelectedIndex = 0;
        FaultComboBox.ItemsSource = Enum.GetValues<CorrectionSurfaceFault>();
        FaultComboBox.SelectedIndex = 0;
        _timer = DispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += OnReleaseTimerTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        CancelActive(CorrectionApplicationReason.Navigation);
        base.OnNavigatedFrom(e);
    }

    public void DisposeResources()
    {
        if (_isDisposed)
        {
            return;
        }

        // Lifecycle cancellation wins over any earlier poison. Only after the
        // lease is terminal do we discard composition authority.
        CancelActive(CorrectionApplicationReason.Disposed);
        _composition = CompositionAuthority.Unknown;
        UnhookLifecycleAuthorities();
        _timer.Tick -= OnReleaseTimerTick;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _isDisposed = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded || _isDisposed)
        {
            return;
        }

        _isLoaded = true;
        PlaygroundShell.WindowActivationChanged += OnWindowActivationChanged;
        _readOnlyCallbackToken = Editor.RegisterPropertyChangedCallback(
            RichEditBox.IsReadOnlyProperty,
            OnReadOnlyChanged);

        if (_surface.Generation == 0)
        {
            LoadScenario();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelActive(CorrectionApplicationReason.Unloaded);
        _composition = CompositionAuthority.Unknown;
        UnhookLifecycleAuthorities();
    }

    private void UnhookLifecycleAuthorities()
    {
        if (!_isLoaded)
        {
            return;
        }

        PlaygroundShell.WindowActivationChanged -= OnWindowActivationChanged;
        Editor.UnregisterPropertyChangedCallback(
            RichEditBox.IsReadOnlyProperty,
            _readOnlyCallbackToken);
        _readOnlyCallbackToken = 0;
        _isLoaded = false;
    }

    private void OnLoadScenarioClick(object sender, RoutedEventArgs e)
        => LoadScenario();

    private void LoadScenario()
    {
        CancelActive(CorrectionApplicationReason.Superseded);
        _fixture = FixtureComboBox.SelectedItem as CorrectionApplicationFixture
            ?? CorrectionApplicationFixtures.All[0];
        _suppressObservation = true;
        bool loaded;
        try
        {
            loaded = _surface.Reset(_fixture.Body);
        }
        finally
        {
            _suppressObservation = false;
        }

        _composition = CompositionAuthority.Unknown;
        Editor.Focus(FocusState.Programmatic);
        StatusText.Text = loaded
            ? "Scenario loaded. Type after the final space, then arm the delayed edit."
            : "Scenario unavailable: the TOM-to-.NET text mapping was not exact.";
    }

    private void OnArmClick(object sender, RoutedEventArgs e)
    {
        CancelActive(CorrectionApplicationReason.Superseded);
        _fault = ReadFault();

        // The button owns focus on Click. Return it before a lease exists, so
        // the focus transition cannot poison the lease we are about to create.
        // GotFocus can be delivered after this Click handler continues. A
        // completed focus transfer is already a neutral composition boundary:
        // the UI thread cannot interleave new text input before the lease is
        // created, and later composition events still poison that lease.
        if (Editor.Focus(FocusState.Programmatic) && HasEditorFocus())
        {
            _composition = CompositionAuthority.Neutral;
        }

        if (!_surface.ArmDiagnosticSentence(
                _fixture.SentenceStart,
                _fixture.SentenceLength,
                _fixture.SentenceLiteral))
        {
            _gateEvidence = default(AttemptGateEvidence) with
            {
                DiagnosticSentence = false,
            };
            RecordImmediate(
                CorrectionApplicationOutcome.Abstained,
                CorrectionApplicationReason.DiagnosticSentenceChanged);
            return;
        }

        if (!_surface.TrySnapshot(
                _fixture.Edit,
                _fixture.SentenceLiteral,
                out var snapshot))
        {
            _surface.ClearDiagnosticSentence();
            RecordImmediate(
                CorrectionApplicationOutcome.Abstained,
                CorrectionApplicationReason.ApiFailure);
            return;
        }

        CorrectionApplicationReason snapshotRejection = snapshot switch
        {
            { IsTomMappingExact: false } => CorrectionApplicationReason.UnsupportedTomMapping,
            { IsDiagnosticSentenceExact: false } => CorrectionApplicationReason.DiagnosticSentenceChanged,
            { IsTargetRangeExact: false } => CorrectionApplicationReason.TargetRangeChanged,
            _ => CorrectionApplicationReason.None,
        };
        if (snapshotRejection != CorrectionApplicationReason.None)
        {
            _gateEvidence = new AttemptGateEvidence(
                ExactText: null,
                ExactSelection: null,
                Focus: null,
                Activation: null,
                Writable: null,
                Composition: null,
                TargetGeneration: null,
                TomMapping: snapshot.IsTomMappingExact,
                DiagnosticSentence: snapshot.IsDiagnosticSentenceExact,
                TargetRange: snapshot.IsTargetRangeExact);
            _surface.ClearDiagnosticSentence();
            RecordImmediate(CorrectionApplicationOutcome.Abstained, snapshotRejection);
            return;
        }

        PlaygroundWindowActivation activation =
            PlaygroundShell.ReadWindowActivation?.Invoke() ?? default;

        bool hasEditorFocus = HasEditorFocus();
        bool isActive = activation.IsKnown && activation.IsActive;
        bool isWritable = !Editor.IsReadOnly;
        bool isCompositionNeutral = _composition == CompositionAuthority.Neutral;
        _gateEvidence = new AttemptGateEvidence(
            ExactText: true,
            ExactSelection: snapshot.Selection.IsDegenerate
                && snapshot.Selection.Start == snapshot.Body.Length,
            Focus: hasEditorFocus,
            Activation: isActive,
            Writable: isWritable,
            Composition: isCompositionNeutral,
            TargetGeneration: true,
            TomMapping: true,
            DiagnosticSentence: true,
            TargetRange: true);

        var arm = new CorrectionApplicationArmState(
            snapshot.Body,
            _fixture.SentenceStart,
            _fixture.SentenceLength,
            _fixture.SentenceLiteral,
            _fixture.Edit,
            snapshot.Selection,
            _surface.Generation,
            activation.Generation,
            hasEditorFocus,
            isActive,
            !isWritable,
            isCompositionNeutral);

        if (!CorrectionApplicationTrial.TryArm(arm, out _trial, out var reason))
        {
            _surface.ClearDiagnosticSentence();
            RecordImmediate(CorrectionApplicationOutcome.Abstained, reason);
            return;
        }

        _releaseAuthorityFailure = null;
        _releaseSnapshot = null;
        _postSelection = null;
        _executionEvidence = default;
        _actualReleaseDelayMs = null;

        _requestedDelayMs = ReadDelayMilliseconds();
        _timer.Interval = TimeSpan.FromMilliseconds(_requestedDelayMs);
        _delayClock.Restart();
        _timer.Start();
        StatusText.Text = $"Armed for {_requestedDelayMs} ms. Keep typing at the end of the document.";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
        => CancelActive(CorrectionApplicationReason.UserCancelled);

    private void OnCopyEvidenceClick(object sender, RoutedEventArgs e)
    {
        var export = new
        {
            schema = "deckle.acx0021.phase_a.evidence.v1",
            privacy = "Synthetic fixtures; content-free; timing and cadence coarsened; selection endpoints omitted.",
            claim_boundary = "Owned WinUI RichEditBox phase A only. No external application, UIA, production, end-to-end, field-quality, model, or GPU claim.",
            attempts = _attempts.Select(attempt => new
            {
                attempt.Index,
                fixture = attempt.FixtureName,
                fault = attempt.Fault.ToString(),
                attempt.RequestedDelayMs,
                actual_delay_bucket_ms = CorrectionEvidencePrivacy.CoarsenMilliseconds(
                    attempt.ActualDelayMs,
                    10),
                overshoot_bucket_ms = CorrectionEvidencePrivacy.CoarsenMilliseconds(
                    attempt.OvershootMs,
                    10),
                release_duration_bucket_ms = Math.Round(
                    attempt.ReleaseDurationMs,
                    1,
                    MidpointRounding.AwayFromZero),
                appended_utf16_bucket = CorrectionEvidencePrivacy.CountBucket(
                    attempt.AppendedUtf16Units),
                text_change_event_bucket = CorrectionEvidencePrivacy.CountBucket(
                    attempt.TextChangeEvents),
                attempt.EditLengthDelta,
                outcome = attempt.Outcome.ToString(),
                reason = attempt.Reason.ToString(),
                attempt.Gates,
                attempt.ExactAppliedText,
                attempt.ExactAppliedSelection,
                attempt.ExactUndoText,
                attempt.ExactUndoSelection,
                attempt.ExactRedoText,
                attempt.ExactRedoSelection,
                attempt.FocusPostcondition,
            }),
        };
        string json = JsonSerializer.Serialize(
            export,
            new JsonSerializerOptions { WriteIndented = true });
        ClipboardWriteResult result = Win32Clipboard.TryCopyText(json);
        StatusText.Text = result.Landed
            ? $"Copied {_attempts.Count} content-free attempts."
            : $"Evidence copy failed: {result.Status}.";
    }

    private void OnReleaseTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_trial is null)
        {
            return;
        }

        _actualReleaseDelayMs = _delayClock.ElapsedMilliseconds;
        _releaseClock.Restart();

        if (!_surface.TrySnapshot(_fixture.Edit, _fixture.SentenceLiteral, out var snapshot))
        {
            _trial.Poison(CorrectionApplicationReason.ApiFailure);
            FinishPreparation(_trial.Prepare(default));
            return;
        }

        PlaygroundWindowActivation activation =
            PlaygroundShell.ReadWindowActivation?.Invoke() ?? default;

        _releaseSnapshot = snapshot;
        _gateEvidence = new AttemptGateEvidence(
            ExactText: snapshot.Body.StartsWith(_trial.ArmedBody, StringComparison.Ordinal),
            ExactSelection: snapshot.Selection.IsDegenerate
                && snapshot.Selection.Start == snapshot.Body.Length,
            Focus: HasEditorFocus(),
            Activation: activation.IsKnown && activation.IsActive,
            Writable: !Editor.IsReadOnly,
            Composition: _composition == CompositionAuthority.Neutral,
            TargetGeneration: _surface.Generation == _trial.TargetGeneration
                && activation.Generation == _trial.WindowActivationGeneration,
            TomMapping: snapshot.IsTomMappingExact,
            DiagnosticSentence: snapshot.IsDiagnosticSentenceExact,
            TargetRange: snapshot.IsTargetRangeExact);

        var release = new CorrectionApplicationReleaseState(
            snapshot.Body,
            snapshot.Selection,
            _surface.Generation,
            activation.Generation,
            HasEditorFocus(),
            activation.IsKnown && activation.IsActive,
            Editor.IsReadOnly,
            _composition == CompositionAuthority.Neutral,
            snapshot.IsTomMappingExact,
            snapshot.IsDiagnosticSentenceExact,
            snapshot.IsTargetRangeExact);

        FinishPreparation(_trial.Prepare(release));
    }

    private void FinishPreparation(CorrectionApplicationPreparation preparation)
    {
        if (_trial is null)
        {
            return;
        }

        if (!preparation.IsApproved)
        {
            RecordAndClear(preparation.Outcome, preparation.Reason);
            return;
        }

        _suppressObservation = true;
        CorrectionSurfaceExecution execution;
        CorrectionApplicationReason executionReason;
        try
        {
            execution = _surface.Execute(preparation.Plan!, _fault);
            _executionEvidence = execution;
            if (_surface.TryObserve(out _, out var postSelection, out _))
            {
                _postSelection = postSelection;
            }
        }
        finally
        {
            _suppressObservation = false;
        }

        var resolution = CorrectionApplicationCompletion.Resolve(
            execution,
            _releaseAuthorityFailure,
            HasEditorFocus());
        executionReason = resolution.Reason;

        if (resolution.Outcome == CorrectionApplicationOutcome.Applied)
        {
            _trial.CompleteApplied();
            RecordAndClear(
                CorrectionApplicationOutcome.Applied,
                CorrectionApplicationReason.None);
        }
        else if (resolution.Outcome == CorrectionApplicationOutcome.Abstained)
        {
            _trial.CompleteAbstained(executionReason);
            RecordAndClear(
                CorrectionApplicationOutcome.Abstained,
                executionReason);
        }
        else
        {
            _trial.CompleteIntegrityFailure(executionReason);
            RecordAndClear(
                CorrectionApplicationOutcome.IntegrityFailure,
                executionReason);
        }
    }

    private void OnEditorTextChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressObservation
            || _trial?.State is not (CorrectionApplicationState.ArmedSafe
                or CorrectionApplicationState.Poisoned))
        {
            return;
        }

        if (_surface.TrySnapshot(_fixture.Edit, _fixture.SentenceLiteral, out var snapshot))
        {
            _trial.ObserveBodyChange(snapshot.Body);
            if (_trial.State == CorrectionApplicationState.Poisoned)
            {
                _gateEvidence = ApplyHistoricalFailure(_gateEvidence, _trial.Reason);
            }
        }
        else
        {
            _trial.Poison(CorrectionApplicationReason.ApiFailure);
        }
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressObservation
            || _trial?.State is not (CorrectionApplicationState.ArmedSafe
                or CorrectionApplicationState.Poisoned))
        {
            return;
        }

        if (_surface.TrySnapshot(_fixture.Edit, _fixture.SentenceLiteral, out var snapshot))
        {
            _trial.ObserveSelection(snapshot.Selection, snapshot.Body.Length);
            if (_trial.State == CorrectionApplicationState.Poisoned)
            {
                _gateEvidence = ApplyHistoricalFailure(_gateEvidence, _trial.Reason);
            }
        }
        else
        {
            _trial.Poison(CorrectionApplicationReason.ApiFailure);
        }
    }

    private void OnEditorGotFocus(object sender, RoutedEventArgs e)
    {
        if (_trial is null)
        {
            _composition = CompositionAuthority.Neutral;
        }
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        _composition = CompositionAuthority.Unknown;
        InvalidateAuthority(CorrectionApplicationReason.FocusLost);
    }

    private void OnTextCompositionStarted(
        RichEditBox sender,
        TextCompositionStartedEventArgs args)
    {
        _composition = CompositionAuthority.Active;
        InvalidateAuthority(CorrectionApplicationReason.CompositionStarted);
    }

    private void OnTextCompositionChanged(
        RichEditBox sender,
        TextCompositionChangedEventArgs args)
    {
        if (_composition != CompositionAuthority.Active)
        {
            _composition = CompositionAuthority.Unknown;
            InvalidateAuthority(CorrectionApplicationReason.CompositionUncertain);
        }
    }

    private void OnTextCompositionEnded(
        RichEditBox sender,
        TextCompositionEndedEventArgs args)
    {
        if (_composition == CompositionAuthority.Active)
        {
            _composition = CompositionAuthority.Neutral;
        }
        else
        {
            _composition = CompositionAuthority.Unknown;
            InvalidateAuthority(CorrectionApplicationReason.CompositionUncertain);
        }
    }

    private void OnReadOnlyChanged(DependencyObject sender, DependencyProperty property)
        => InvalidateAuthority(CorrectionApplicationReason.ReadOnlyChanged);

    private void OnWindowActivationChanged(PlaygroundWindowActivation activation)
    {
        _composition = CompositionAuthority.Unknown;
        InvalidateAuthority(CorrectionApplicationReason.WindowActivationChanged);
    }

    private void InvalidateAuthority(CorrectionApplicationReason reason)
    {
        _gateEvidence = ApplyHistoricalFailure(_gateEvidence, reason);
        if (_trial?.State == CorrectionApplicationState.Releasing)
        {
            _releaseAuthorityFailure ??= reason;
        }
        else
        {
            _trial?.Poison(reason);
        }
    }

    private void CancelActive(CorrectionApplicationReason reason)
    {
        _timer.Stop();
        if (_trial is null)
        {
            return;
        }

        _trial.Cancel(reason);
        RecordAndClear(CorrectionApplicationOutcome.Cancelled, reason);
    }

    private void RecordImmediate(
        CorrectionApplicationOutcome outcome,
        CorrectionApplicationReason reason)
    {
        _delayClock.Reset();
        _requestedDelayMs = ReadDelayMilliseconds();
        _actualReleaseDelayMs = null;
        AddAttempt(outcome, reason, appended: 0, textChanges: 0);
    }

    private void RecordAndClear(
        CorrectionApplicationOutcome outcome,
        CorrectionApplicationReason reason)
    {
        int appended = _trial?.AppendedUtf16Units ?? 0;
        int textChanges = _trial?.TextChangeEventCount ?? 0;
        AddAttempt(outcome, reason, appended, textChanges);
        _trial = null;
        _delayClock.Reset();
        _releaseClock.Reset();
        _releaseAuthorityFailure = null;
        _releaseSnapshot = null;
        _postSelection = null;
        _gateEvidence = default;
        _executionEvidence = default;
        _actualReleaseDelayMs = null;
    }

    private void AddAttempt(
        CorrectionApplicationOutcome outcome,
        CorrectionApplicationReason reason,
        int appended,
        int textChanges)
    {
        long? elapsedMs = _actualReleaseDelayMs;
        long? overshootMs = elapsedMs is long actual
            ? Math.Max(0, actual - _requestedDelayMs)
            : null;
        double releaseDurationMs = _releaseClock.IsRunning
            ? _releaseClock.Elapsed.TotalMilliseconds
            : 0;
        var beforeSelection = _releaseSnapshot?.Selection;
        AttemptGateEvidence gates = ApplyGateFailures(
            ApplyHistoricalFailure(_gateEvidence, reason),
            _trial?.GateFailures ?? CorrectionApplicationGateFailure.None);
        var attempt = new CorrectionApplicationAttempt(
            Index: checked(++_attemptIndex),
            FixtureName: _fixture.Name,
            Fault: _fault,
            RequestedDelayMs: _requestedDelayMs,
            ActualDelayMs: elapsedMs,
            OvershootMs: overshootMs,
            ReleaseDurationMs: releaseDurationMs,
            AppendedUtf16Units: appended,
            TextChangeEvents: textChanges,
            BeforeSelectionStart: beforeSelection?.Start,
            BeforeSelectionEnd: beforeSelection?.End,
            AfterSelectionStart: _postSelection?.Start,
            AfterSelectionEnd: _postSelection?.End,
            EditLengthDelta: _fixture.Edit.LengthDelta,
            Outcome: outcome,
            Reason: reason,
            Gates: gates,
            ExactAppliedText: _executionEvidence.ExactAppliedText,
            ExactAppliedSelection: _executionEvidence.ExactAppliedSelection,
            ExactUndoText: _executionEvidence.ExactUndoText,
            ExactUndoSelection: _executionEvidence.ExactUndoSelection,
            ExactRedoText: _executionEvidence.ExactRedoText,
            ExactRedoSelection: _executionEvidence.ExactRedoSelection,
            FocusPostcondition: outcome == CorrectionApplicationOutcome.Applied
                ? true
                : reason == CorrectionApplicationReason.FocusPostcondition
                    ? false
                    : null);
        _attempts.Add(attempt);
        AttemptList.Items.Insert(0, attempt);
        StatusText.Text = attempt.ToString();
    }

    private int ReadDelayMilliseconds()
        => DelayComboBox.SelectedItem is ComboBoxItem item
           && int.TryParse(item.Tag?.ToString(), out int delay)
            ? delay
            : 500;

    private CorrectionSurfaceFault ReadFault()
        => FaultComboBox.SelectedItem is CorrectionSurfaceFault fault
            ? fault
            : CorrectionSurfaceFault.None;

    private bool HasEditorFocus()
        => Editor.XamlRoot is not null
           && ReferenceEquals(FocusManager.GetFocusedElement(Editor.XamlRoot), Editor)
           && Editor.FocusState != FocusState.Unfocused;

    private static AttemptGateEvidence ApplyHistoricalFailure(
        AttemptGateEvidence evidence,
        CorrectionApplicationReason reason)
        => reason switch
        {
            CorrectionApplicationReason.NonAppendTextChange
                or CorrectionApplicationReason.PrefixChanged => evidence with { ExactText = false },
            CorrectionApplicationReason.SelectionChanged
                or CorrectionApplicationReason.InitialSelection => evidence with { ExactSelection = false },
            CorrectionApplicationReason.FocusLost
                or CorrectionApplicationReason.InitialFocus => evidence with { Focus = false },
            CorrectionApplicationReason.WindowActivationChanged
                or CorrectionApplicationReason.InitialWindowActivation => evidence with { Activation = false },
            CorrectionApplicationReason.ReadOnlyChanged
                or CorrectionApplicationReason.InitialReadOnly => evidence with { Writable = false },
            CorrectionApplicationReason.CompositionStarted
                or CorrectionApplicationReason.CompositionUncertain
                or CorrectionApplicationReason.InitialComposition => evidence with { Composition = false },
            CorrectionApplicationReason.TargetGenerationChanged => evidence with { TargetGeneration = false },
            CorrectionApplicationReason.UnsupportedTomMapping => evidence with { TomMapping = false },
            CorrectionApplicationReason.DiagnosticSentenceChanged => evidence with { DiagnosticSentence = false },
            CorrectionApplicationReason.TargetRangeChanged
                or CorrectionApplicationReason.LiteralMismatch => evidence with { TargetRange = false },
            _ => evidence,
        };

    private static AttemptGateEvidence ApplyGateFailures(
        AttemptGateEvidence evidence,
        CorrectionApplicationGateFailure failures)
    {
        if (failures.HasFlag(CorrectionApplicationGateFailure.Text))
            evidence = evidence with { ExactText = false };
        if (failures.HasFlag(CorrectionApplicationGateFailure.Selection))
            evidence = evidence with { ExactSelection = false };
        if (failures.HasFlag(CorrectionApplicationGateFailure.Focus))
            evidence = evidence with { Focus = false };
        if (failures.HasFlag(CorrectionApplicationGateFailure.Activation))
            evidence = evidence with { Activation = false };
        if (failures.HasFlag(CorrectionApplicationGateFailure.Writable))
            evidence = evidence with { Writable = false };
        if (failures.HasFlag(CorrectionApplicationGateFailure.Composition))
            evidence = evidence with { Composition = false };
        if (failures.HasFlag(CorrectionApplicationGateFailure.TargetGeneration))
            evidence = evidence with { TargetGeneration = false };
        if (failures.HasFlag(CorrectionApplicationGateFailure.TomMapping))
            evidence = evidence with { TomMapping = false };
        if (failures.HasFlag(CorrectionApplicationGateFailure.DiagnosticSentence))
            evidence = evidence with { DiagnosticSentence = false };
        if (failures.HasFlag(CorrectionApplicationGateFailure.TargetRange))
            evidence = evidence with { TargetRange = false };
        return evidence;
    }

    private enum CompositionAuthority
    {
        Unknown,
        Neutral,
        Active,
    }

    private readonly record struct AttemptGateEvidence(
        bool? ExactText,
        bool? ExactSelection,
        bool? Focus,
        bool? Activation,
        bool? Writable,
        bool? Composition,
        bool? TargetGeneration,
        bool? TomMapping,
        bool? DiagnosticSentence,
        bool? TargetRange);

    private sealed record CorrectionApplicationAttempt(
        long Index,
        string FixtureName,
        CorrectionSurfaceFault Fault,
        int RequestedDelayMs,
        long? ActualDelayMs,
        long? OvershootMs,
        double ReleaseDurationMs,
        int AppendedUtf16Units,
        int TextChangeEvents,
        int? BeforeSelectionStart,
        int? BeforeSelectionEnd,
        int? AfterSelectionStart,
        int? AfterSelectionEnd,
        int EditLengthDelta,
        CorrectionApplicationOutcome Outcome,
        CorrectionApplicationReason Reason,
        AttemptGateEvidence Gates,
        bool? ExactAppliedText,
        bool? ExactAppliedSelection,
        bool? ExactUndoText,
        bool? ExactUndoSelection,
        bool? ExactRedoText,
        bool? ExactRedoSelection,
        bool? FocusPostcondition)
    {
        public override string ToString()
        {
            string delay = ActualDelayMs is long actual
                ? $"{actual} ms ({OvershootMs} ms over)"
                : "not released";
            return $"#{Index} · {Outcome} · {Reason} · {delay} · release {ReleaseDurationMs:F3} ms · appended {AppendedUtf16Units} UTF-16 · {TextChangeEvents} text events";
        }
    }
}
