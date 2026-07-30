using Deckle.Autocorrect;
using Deckle.Core;
using Deckle.Input;
using Deckle.Llm.Rewrite;
using System.Collections.Concurrent;

namespace Deckle.App;

// Host bridge for the paragraph-retaille interaction. Business state and UI
// live in Deckle.Llm.Rewrite; this file only maps the process-shared Raw Input
// stream onto that public surface and restores the captured target before an
// accepted offer is injected.
public partial class App
{
    private ParagraphDraft? _paragraphDraft;
    private ParagraphRewriteCoordinator? _paragraphRewriteCoordinator;
    private RewriteOfferWindow? _paragraphRewriteWindow;
    private KeyDecoder? _paragraphKeyDecoder;
    private SurfaceProber? _paragraphSurfaceProber;
    private FocusedSurface _paragraphSurface = FocusedSurface.Unknown;
    private TextInjector? _paragraphInjector;
    private ICaretTextReader? _paragraphCaretTextReader;
    private readonly ConcurrentQueue<ParagraphCaretRecovery> _paragraphCaretRecoveries = new();
    private IntPtr _paragraphTargetHwnd;
    private int _paragraphRecoveryRevision;
    private volatile bool _paragraphOfferVisible;
    private bool _paragraphRewriteStarted;

    private void InitializeParagraphRewrite()
    {
        if (_keyboardMouseHost is null || _paragraphRewriteStarted) return;

        _paragraphDraft = new ParagraphDraft();
        _paragraphKeyDecoder = new KeyDecoder();
        _paragraphSurfaceProber = new SurfaceProber();
        _paragraphInjector = new TextInjector();
        _paragraphCaretTextReader = new UIAutomationCaretTextReader();
        _paragraphRewriteCoordinator = new ParagraphRewriteCoordinator(
            new RewriteService(),
            () => LlmSettingsService.Instance.Current.OllamaEndpoint);
        _paragraphRewriteWindow = new RewriteOfferWindow();

        _paragraphRewriteCoordinator.OfferReady += OnParagraphOfferReady;
        _paragraphRewriteCoordinator.OfferInvalidated += HideParagraphOffer;
        _paragraphRewriteWindow.ApplyRequested += ApplyParagraphOffer;
        _paragraphRewriteWindow.KeptOriginal += KeepParagraphOriginal;

        _keyboardMouseHost.KeyReceived += OnParagraphKey;
        _keyboardMouseHost.PointerInteraction += OnParagraphPointerInteraction;
        _keyboardMouseHost.FocusChanged += OnParagraphFocusChanged;
        _keyboardMouseHost.DrainRequested += OnParagraphDrainRequested;
        if (!_keyboardMouseHost.Start())
        {
            ShutdownParagraphRewrite();
            return;
        }

        _paragraphRewriteStarted = true;
        _paragraphSurface = _paragraphSurfaceProber.Probe();
    }

    private void OnParagraphKey(KeyboardKeyEvent e)
    {
        if (e.IsInjected || _paragraphKeyDecoder is null || _paragraphDraft is null
            || _paragraphRewriteCoordinator is null)
            return;

        Keystroke? stroke = _paragraphKeyDecoder.Decode(e);
        if (stroke is null) return;

        // Once the user explicitly enters the inset, its native controls own
        // Tab, Enter, and Escape. The global observer must not interpret those
        // keys as edits in the target paragraph.
        if (_paragraphOfferVisible
            && _paragraphRewriteWindow is not null
            && NativeMethods.GetForegroundWindow() == _paragraphRewriteWindow.Hwnd)
            return;

        int revision = Interlocked.Increment(ref _paragraphRecoveryRevision);

        if (!LlmSettingsService.Instance.Current.Enabled
            || !_paragraphSurface.IsTextEditable
            || _paragraphSurface.IsPassword)
        {
            _paragraphDraft.Invalidate();
            _paragraphRewriteCoordinator.Invalidate();
            return;
        }

        Keystroke key = stroke.Value;
        switch (key.Kind)
        {
            case KeystrokeKind.Text:
                _paragraphRewriteCoordinator.Invalidate();
                _paragraphDraft.Append(key.Text);
                break;

            case KeystrokeKind.Backspace:
                _paragraphRewriteCoordinator.Invalidate();
                _paragraphDraft.Backspace();
                break;

            case KeystrokeKind.Enter when _paragraphKeyDecoder.ShiftDown:
                HideParagraphOffer();
                if (_paragraphDraft.TryClose(out string paragraph))
                {
                    _paragraphTargetHwnd = NativeMethods.GetForegroundWindow();
                    _paragraphRewriteCoordinator.Request(paragraph);
                }
                else
                {
                    RequestRecoveredParagraph(revision);
                }
                break;

            case KeystrokeKind.Enter:
                _paragraphRewriteCoordinator.Invalidate();
                _paragraphDraft.Reset();
                break;

            case KeystrokeKind.Other:
                _paragraphRewriteCoordinator.Invalidate();
                _paragraphDraft.Invalidate();
                break;

            default:
                _paragraphRewriteCoordinator.Invalidate();
                _paragraphDraft.Invalidate();
                break;
        }
    }

    private void OnParagraphPointerInteraction()
    {
        if (_paragraphOfferVisible && _paragraphRewriteWindow?.ContainsPointer() == true)
            return;

        Interlocked.Increment(ref _paragraphRecoveryRevision);
        _paragraphDraft?.Invalidate();
        _paragraphRewriteCoordinator?.Invalidate();
    }

    private void OnParagraphFocusChanged()
    {
        if (_paragraphRewriteWindow is not null
            && NativeMethods.GetForegroundWindow() == _paragraphRewriteWindow.Hwnd)
            return;

        Interlocked.Increment(ref _paragraphRecoveryRevision);
        _paragraphDraft?.Invalidate();
        _paragraphRewriteCoordinator?.Invalidate();
        if (_paragraphSurfaceProber is not null)
            _paragraphSurface = _paragraphSurfaceProber.Probe();
    }

    private void OnParagraphOfferReady(ParagraphRewriteOffer offer)
    {
        RewriteOfferWindow? window = _paragraphRewriteWindow;
        if (window is null) return;

        window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_paragraphRewriteCoordinator?.IsCurrent(offer.Revision) != true
                || _paragraphTargetHwnd == IntPtr.Zero
                || NativeMethods.GetForegroundWindow() != _paragraphTargetHwnd)
                return;

            ScreenRect anchor;
            if (!UIAutomation.TryGetFocusedElementBounds(out anchor))
            {
                if (!NativeMethods.GetWindowRect(_paragraphTargetHwnd, out NativeMethods.RECT rect))
                    return;
                anchor = new ScreenRect(
                    rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
            }

            _paragraphOfferVisible = true;
            window.ShowOffer(offer, _paragraphTargetHwnd, anchor);
        });
    }

    private void ApplyParagraphOffer(ParagraphRewriteOffer offer)
    {
        _paragraphOfferVisible = false;
        IntPtr target = _paragraphTargetHwnd;
        _paragraphTargetHwnd = IntPtr.Zero;
        if (_paragraphRewriteCoordinator?.IsCurrent(offer.Revision) != true)
            return;

        if (target == IntPtr.Zero || _paragraphInjector is null) return;

        // The offer window still owns the foreground here. That makes this a
        // user-authorized activation transfer and, critically, keeps the focus
        // event caused by hiding the offer from invalidating the revision before
        // the replacement has been attempted.
        if (!NativeMethods.SetForegroundWindow(target)
            || NativeMethods.GetForegroundWindow() != target)
            return;

        if (!_paragraphInjector.ReplaceClosedParagraph(offer.Original, offer.Rewritten))
            return;

        _paragraphDraft?.Reset();
        _paragraphRewriteCoordinator?.Invalidate();
    }

    private void KeepParagraphOriginal()
    {
        _paragraphOfferVisible = false;
        _paragraphTargetHwnd = IntPtr.Zero;
        _paragraphRewriteCoordinator?.Invalidate();
    }

    private void HideParagraphOffer()
    {
        RewriteOfferWindow? window = _paragraphRewriteWindow;
        if (!_paragraphOfferVisible || window is null) return;
        _paragraphOfferVisible = false;
        window.DispatcherQueue.TryEnqueue(window.Hide);
    }

    private void OnParagraphCorrectionApplied(CorrectionDecision decision)
    {
        Interlocked.Increment(ref _paragraphRecoveryRevision);
        _paragraphDraft?.ApplyCorrection(decision.Original, decision.Replacement);
        _paragraphRewriteCoordinator?.Invalidate();
    }

    private void RequestRecoveredParagraph(int revision)
    {
        ICaretTextReader? reader = _paragraphCaretTextReader;
        IKeyboardInputHost? host = _keyboardMouseHost;
        if (reader is null || host is null) return;

        _ = Task.Run(() =>
        {
            bool succeeded = reader.TryReadStable(out FocusedCaretText text, out _);
            _paragraphCaretRecoveries.Enqueue(new ParagraphCaretRecovery(revision, succeeded, text));
            host.RequestDrain();
        });
    }

    private void OnParagraphDrainRequested()
    {
        while (_paragraphCaretRecoveries.TryDequeue(out ParagraphCaretRecovery recovery))
        {
            if (!recovery.Succeeded
                || recovery.Revision != Volatile.Read(ref _paragraphRecoveryRevision)
                || _paragraphRewriteCoordinator is null
                || !LlmSettingsService.Instance.Current.Enabled
                || !_paragraphSurface.IsTextEditable
                || _paragraphSurface.IsPassword)
                continue;

            CaretParagraphContextResult context = CaretParagraphContext.ExtractClosed(
                recovery.Text.TextBeforeCaret,
                recovery.Text.ReachedDocumentStart);
            if (!context.Available || recovery.Text.ForegroundWindow == 0)
                continue;

            _paragraphTargetHwnd = new IntPtr(recovery.Text.ForegroundWindow);
            _paragraphRewriteCoordinator.Request(context.Text);
        }
    }

    private void ShutdownParagraphRewrite()
    {
        if (_keyboardMouseHost is not null)
        {
            _keyboardMouseHost.KeyReceived -= OnParagraphKey;
            _keyboardMouseHost.PointerInteraction -= OnParagraphPointerInteraction;
            _keyboardMouseHost.FocusChanged -= OnParagraphFocusChanged;
            _keyboardMouseHost.DrainRequested -= OnParagraphDrainRequested;
            if (_paragraphRewriteStarted)
                _keyboardMouseHost.Stop();
        }

        _paragraphRewriteCoordinator?.Dispose();
        _paragraphRewriteWindow?.Hide();
        _paragraphRewriteCoordinator = null;
        _paragraphRewriteWindow = null;
        _paragraphCaretTextReader = null;
        Interlocked.Increment(ref _paragraphRecoveryRevision);
        _paragraphRewriteStarted = false;
    }

    private readonly record struct ParagraphCaretRecovery(
        int Revision,
        bool Succeeded,
        FocusedCaretText Text);
}
