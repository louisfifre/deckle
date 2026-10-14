using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Deckle.Composition;
using Deckle.Settings;

namespace Deckle.Hud;

// Chrono card — container + clock + processing stroke attach.
//
// The permanent frame is the DWM border on the rounded HWND
// (DWMWA_BORDER_COLOR = DWMWA_COLOR_DEFAULT in HudWindow); the Composition
// processing stroke composites on top of it for Recording, Transcribing and
// Rewriting.
//
// The vsync rendering hook (CompositionTarget.Rendering) drives the clock:
// no DispatcherTimer, so no jitter when the UI thread is busy.
public sealed partial class HudChrono : UserControl
{
    private bool _renderingHooked;

    private HudState _state = HudState.Hidden;
    private bool _animationsEnabled = SettingsService.Instance.Current.Overlay.Animations;
    private bool _animationPreferenceSubscribed;

    public HudChrono()
    {
        InitializeComponent();

        // Build the digit arrays now: ClearDigitHeat (called by ApplyCharging
        // on cold boot) early-returns while they are null and leaves the face
        // blank instead of 00.00.00.
        EnsureRevealInfrastructure();

        CountdownSurfaceHost.SizeChanged += (_, _) => RebuildCountdownVisual();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        ChronoRoot.ActualThemeChanged += (_, _) =>
        {
            // The face's background tone is mutated onto Foreground in code
            // (ApplyRestTone), which drops the {ThemeResource} binding — so a
            // live theme switch must re-push the current phase's tone. The
            // accent twins keep their XAML accent brush and need no refresh.
            ReapplyRestToneForState();
            RebuildCountdownVisual();

            // The grey material has one lightness per theme, and any state
            // may show it, so every state re-applies its variant; on the
            // colour material the blend lands on the values already shown.
            // Under _strokeSync: the audio pump may be writing to the stroke.
            lock (_strokeSync)
            {
                if (_processingStroke != null && _currentVariant is { } v)
                {
                    _processingStroke.ApplyVariant(
                        v, ChronoRoot.ActualTheme == ElementTheme.Dark);
                }
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_animationPreferenceSubscribed) return;
        _animationPreferenceSubscribed = true;
        _animationsEnabled = SettingsService.Instance.Current.Overlay.Animations;
        SettingsService.Instance.OverlayAnimationsChanged += OnOverlayAnimationsChanged;
        HudSettingsService.Instance.Changed += OnHudSettingsChanged;
        SetCountdownRecording(_state == HudState.Recording);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_animationPreferenceSubscribed) return;
        _animationPreferenceSubscribed = false;
        SettingsService.Instance.OverlayAnimationsChanged -= OnOverlayAnimationsChanged;
        HudSettingsService.Instance.Changed -= OnHudSettingsChanged;
        SetCountdownRecording(false);
    }

    // Changed fires on the store's writer thread, after the disk write. The
    // stroke and the countdown bake their config into Win2D surfaces, so a new
    // config means a rebuild, not a property change.
    private void OnHudSettingsChanged()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyStoredSettings();
            return;
        }

        DispatcherQueue.TryEnqueue(ApplyStoredSettings);
    }

    private void ApplyStoredSettings()
    {
        // Under _strokeSync: the audio pump may be writing to the stroke.
        lock (_strokeSync)
        {
            if (_processingStroke is not null)
                RebuildStroke(HudSettingsService.Instance.EffectiveStroke());
        }

        // After the stroke: the gauge reads the config the stroke was built
        // from, so the other order would rebuild it on the old one.
        RebuildCountdownVisual();
    }

    private void OnOverlayAnimationsChanged(bool enabled)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyAnimationPreference(enabled);
            return;
        }

        DispatcherQueue.TryEnqueue(() => ApplyAnimationPreference(enabled));
    }

    private void ApplyAnimationPreference(bool enabled)
    {
        if (_animationsEnabled == enabled) return;
        _animationsEnabled = enabled;
        RebuildCountdownVisual();

        lock (_strokeSync)
        {
            if (_processingStroke is not null && _currentVariant is { } variant)
            {
                _processingStroke.SetAnimationsEnabled(
                    enabled,
                    variant,
                    ChronoRoot.ActualTheme == ElementTheme.Dark);
            }
        }

        if (_revealsActive)
        {
            TearDownReveals();
            EnsureReveals();
            UpdateReveals();
        }
    }

    public void ApplyState(HudState next)
    {
        _state = next;
        SetCountdownRecording(next == HudState.Recording);
        switch (next)
        {
            case HudState.Charging:
                ApplyCharging();
                break;
            case HudState.Recording:
                ApplyRecording();
                break;
            case HudState.Transcribing:
                ApplyTranscribing();
                break;
            case HudState.Rewriting:
                ApplyRewriting();
                break;
            case HudState.Hidden:
            case HudState.Message:
                ApplyHidden();
                break;
        }
    }

    private void ApplyCharging()
    {
        UnhookRendering();
        StopReveal();

        // Parked look: nothing has been recorded yet, so digits and dots alike
        // sit at the faintest step of the scale. Charging only paints the tone;
        // the clock value belongs to the clock lifecycle.
        ApplyRestTone(ToneCharging);

        DetachProcessingVisual();
    }

    private void ApplyRecording()
    {
        StopReveal();

        AttachProcessingVisual(ProcessingVariant.Recording);

        // Digits that advance flip to Accent on top (WriteDigit); the ones that
        // never move stay Secondary, as do the dots. The host calls StartClock
        // right after this.
        ApplyRestTone(ToneRecording);
    }

    private void ApplyTranscribing()
    {
        // The host freezes the clock with StopClock; the processing material
        // stays visible in all six digits over the Stopped tone.
        ApplyRestTone(ToneStopped);

        AttachProcessingVisual(ProcessingVariant.Transcribing);
        HookRendering();
        StartReveal();
        // Rendering retries reveal construction until XAML has produced the
        // glyph masks; the clock is stopped, so UpdateClock is a no-op.
    }

    private void ApplyRewriting()
    {
        ApplyRestTone(ToneStopped);

        AttachProcessingVisual(ProcessingVariant.Rewriting);
        HookRendering();
        StartReveal();
    }

    private void ApplyHidden()
    {
        UnhookRendering();
        StopReveal();

        DetachProcessingVisual();
        // The clock is left as-is (stopped after a take); the next session's
        // ResetClock zeroes it. Rendering is unhooked, so nothing reads a
        // residual value while hidden — it stays invisible and harmless.
    }

    // ── Background tone — the resting colour of the whole face per phase ──────
    //
    // The chrono face never uses Primary: every phase overrides it with one of
    // these three system tones. The accent/reveal layer (Recording flash, Stop
    // material) sits above this background, never replaces it.
    private const string ToneCharging  = "TextFillColorDisabledBrush";   // before any take
    private const string ToneRecording = "TextFillColorSecondaryBrush";  // clock running
    private const string ToneStopped   = "TextFillColorTertiaryBrush";   // frozen, reveal shown

    // The accent twins are left alone: their Opacity is reveal-driven.
    // Mutating Foreground in code drops the XAML {ThemeResource} binding, so a
    // live theme switch is re-pushed by ReapplyRestToneForState.
    private void ApplyRestTone(string brushKey)
    {
        if (Application.Current.Resources[brushKey] is not Brush tone) return;
        Min1.Foreground = tone; Min2.Foreground = tone;
        Sec1.Foreground = tone; Sec2.Foreground = tone;
        Cs1.Foreground  = tone; Cs2.Foreground  = tone;
        DotA.Foreground = tone; DotB.Foreground = tone;
    }

    private void ReapplyRestToneForState()
    {
        switch (_state)
        {
            case HudState.Charging:     ApplyRestTone(ToneCharging);  break;
            case HudState.Recording:    ApplyRestTone(ToneRecording); break;
            case HudState.Transcribing:
            case HudState.Rewriting:    ApplyRestTone(ToneStopped);   break;
            // Hidden / Message: face not shown, nothing to refresh.
        }
    }

    private void HookRendering()
    {
        if (_renderingHooked) return;
        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    private void UnhookRendering()
    {
        if (!_renderingHooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _renderingHooked = false;
    }

    // Single vsync dispatcher for the clock, the countdown and reveal-build
    // retries.
    private void OnRendering(object? sender, object e)
    {
        UpdateClock();
        RenderUtteranceCountdown();
        UpdateReveals();
    }
}
