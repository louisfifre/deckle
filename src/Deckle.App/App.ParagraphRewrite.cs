using Deckle.Autocorrect;
using Deckle.Core;
using Deckle.Input;
using Deckle.Llm.Rewrite;

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
    private IntPtr _paragraphTargetHwnd;
    private volatile bool _paragraphOfferVisible;
    private bool _paragraphRewriteStarted;

    private void InitializeParagraphRewrite()
    {
        if (_keyboardMouseHost is null || _paragraphRewriteStarted) return;

        _paragraphDraft = new ParagraphDraft();
        _paragraphKeyDecoder = new KeyDecoder();
        _paragraphSurfaceProber = new SurfaceProber();
        _paragraphInjector = new TextInjector();
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

        _paragraphDraft?.Invalidate();
        _paragraphRewriteCoordinator?.Invalidate();
    }

    private void OnParagraphFocusChanged()
    {
        if (_paragraphRewriteWindow is not null
            && NativeMethods.GetForegroundWindow() == _paragraphRewriteWindow.Hwnd)
            return;

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

        if (NativeMethods.SetForegroundWindow(target))
            _paragraphInjector.ReplaceClosedParagraph(offer.Original, offer.Rewritten);

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
        => _paragraphDraft?.ApplyCorrection(decision.Original, decision.Replacement);

    private void ShutdownParagraphRewrite()
    {
        if (_keyboardMouseHost is not null)
        {
            _keyboardMouseHost.KeyReceived -= OnParagraphKey;
            _keyboardMouseHost.PointerInteraction -= OnParagraphPointerInteraction;
            _keyboardMouseHost.FocusChanged -= OnParagraphFocusChanged;
            if (_paragraphRewriteStarted)
                _keyboardMouseHost.Stop();
        }

        _paragraphRewriteCoordinator?.Dispose();
        _paragraphRewriteWindow?.Hide();
        _paragraphRewriteCoordinator = null;
        _paragraphRewriteWindow = null;
        _paragraphRewriteStarted = false;
    }
}
