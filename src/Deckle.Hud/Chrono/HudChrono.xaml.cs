using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Deckle.Composition;
using Deckle.Settings;

namespace Deckle.Hud;

// Chrono card — container + clock + processing stroke attach.
//
// Owns the Bitcount Single MM.SS.cc clock and the progressive digit accent
// (each digit that ever changed locks to ChronoAccentBrush until
// the next clock reset). Stroke sources:
//   - DWM frame (always on)     — 1-dip system accent stroke on the rounded
//                                  HWND silhouette (DWMWA_BORDER_COLOR =
//                                  DWMWA_COLOR_DEFAULT in HudWindow). Plays
//                                  the role of the permanent "Windows frame".
//   - Composition accent (state) — 1-dip stroke 1 dip inside the HWND, added
//                                  on top of DWM for Transcribing (diagonal
//                                  gradient) and Rewriting (8 colored arcs).
// The two layers are at different inset positions, so they never overlap
// pixel-wise — DWM at the outer edge, Composition 1 dip inside.
//
// The vsync rendering hook (CompositionTarget.Rendering) drives the clock
// — no DispatcherTimer, no jitter when the UI thread is busy.
//
// Split across per-concern partials (see deckle-modularite):
//   - HudChrono.xaml.cs (this file) — state dispatch, the Apply* paint
//                                     methods, and the shared vsync hook.
//   - HudChrono.Clock.cs            — the chronometer face and its lifecycle
//                                     (ResetClock / StartClock / StopClock).
//   - HudChrono.Reveal.cs           — the static processing-state digit reveal.
//   - HudChrono.Stroke.cs           — the Composition processing stroke.
public sealed partial class HudChrono : UserControl
{
    private bool _renderingHooked;

    private HudState _state = HudState.Hidden;
    private bool _animationsEnabled = SettingsService.Instance.Current.Overlay.Animations;
    private bool _animationPreferenceSubscribed;

    public HudChrono()
    {
        InitializeComponent();

        // Pre-initialize the _digitPrimary / _digitAccent arrays from the
        // ctor. Without this, ClearDigitHeat() (called by ApplyCharging on
        // cold boot during model loading) early-returns on its null check and
        // does not reset opacities, producing a white/empty render instead of
        // "00.00.00" in tertiary color (regression introduced by 7707f09
        // "fix(hud): complementary digit opacities", which adds the accesses
        // without guaranteeing prior initialization). The helper is
        // idempotent, so this has no cost when a reveal starts later.
        EnsureRevealInfrastructure();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        ChronoRoot.ActualThemeChanged += (_, _) =>
        {
            // The face's background tone is mutated onto Foreground in code
            // (ApplyRestTone), which drops the {ThemeResource} binding — so a
            // live theme switch must re-push the current phase's tone. The
            // accent twins keep their XAML accent brush and need no refresh.
            ReapplyRestToneForState();

            // Transcribing exposure is theme-aware (Dark vs Light split),
            // and Recording reuses those same baselines for its greyscale
            // palette — re-apply the variant on live theme change so the
            // stroke brightness matches the new substrate immediately.
            // Rewriting is palette-neutral and doesn't need this pass.
            // Same _strokeSync discipline: touches the stroke the audio pump
            // may be writing, so a live theme switch during recording can't
            // overlap a Dispose/rebuild on the shared field.
            lock (_strokeSync)
            {
                if (_processingStroke != null && _currentVariant is { } v
                    && v != ProcessingVariant.Rewriting)
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
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_animationPreferenceSubscribed) return;
        _animationPreferenceSubscribed = false;
        SettingsService.Instance.OverlayAnimationsChanged -= OnOverlayAnimationsChanged;
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

    // Single state-driven entry point. Called by HudWindow.SetState (which
    // also drives the clock lifecycle around it — see HudChrono.Clock.cs).
    public void ApplyState(HudState next)
    {
        _state = next;
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

        // Parked look: the whole face recedes to the Disabled tone — nothing has
        // been recorded yet, so digits and dots alike sit at the faintest step of
        // the scale (Chrono/CONTEXT.md). The clock value and glyphs are owned by
        // the clock lifecycle (ResetClock, driven by the host); Charging only
        // paints the tone, so the same look can sit over a zeroed or a frozen
        // clock without touching the value.
        ApplyRestTone(ToneCharging);

        DetachProcessingVisual();
    }

    private void ApplyRecording()
    {
        StopReveal();

        AttachProcessingVisual(ProcessingVariant.Recording);

        // Recording background tone: the whole face sits at Secondary. Digits
        // that advance flip to Accent on top (WriteDigit drives the per-tick
        // flash); the ones that never move stay Secondary, as do the dots. The
        // clock face (reset + ticking + flash) is owned by StartClock, which the
        // host calls right after this. See Chrono/CONTEXT.md.
        ApplyRestTone(ToneRecording);
    }

    private void ApplyTranscribing()
    {
        // Stop tone: the whole face drops to Tertiary. The clock is frozen by
        // StopClock (host-driven around this). The processing material remains
        // visible in all six digits over the Tertiary background.
        ApplyRestTone(ToneStopped);

        AttachProcessingVisual(ProcessingVariant.Transcribing);
        HookRendering();
        StartReveal();
        // Rendering retries reveal construction until XAML has produced the
        // glyph masks; the clock is stopped, so UpdateClock is a no-op.
    }

    private void ApplyRewriting()
    {
        // Clock already frozen by the Transcribing transition; Rewriting only
        // re-skins the stroke and restarts the reveal over the same Tertiary
        // Stop tone.
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
    // these three system tones, stepping down the scale Disabled → Secondary →
    // Tertiary. The accent/reveal layer (Recording flash, Stop material) sits above
    // this background, never replaces it. The authoritative mapping lives in
    // Chrono/CONTEXT.md; these keys are its code mirror.
    private const string ToneCharging  = "TextFillColorDisabledBrush";   // before any take
    private const string ToneRecording = "TextFillColorSecondaryBrush";  // clock running
    private const string ToneStopped   = "TextFillColorTertiaryBrush";   // frozen, reveal shown

    // Paint one background tone across the whole face — the 6 digit primaries
    // and the 2 dots, uniformly. The accent twins are left alone (their Opacity
    // is reveal-driven). Mutating Foreground in code drops the XAML
    // {ThemeResource} binding, so a live theme switch is re-pushed by
    // ReapplyRestToneForState off the current state.
    private void ApplyRestTone(string brushKey)
    {
        if (Application.Current.Resources[brushKey] is not Brush tone) return;
        Min1.Foreground = tone; Min2.Foreground = tone;
        Sec1.Foreground = tone; Sec2.Foreground = tone;
        Cs1.Foreground  = tone; Cs2.Foreground  = tone;
        DotA.Foreground = tone; DotB.Foreground = tone;
    }

    // Re-apply the current phase's background tone after a live theme change.
    // The system tone brushes resolve per theme, and ApplyRestTone froze a
    // concrete brush onto each Foreground, so without this the face would keep
    // the pre-switch colours until the next state transition.
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

    // Single vsync dispatcher for the clock and reveal-build retries.
    private void OnRendering(object? sender, object e)
    {
        UpdateClock();
        UpdateReveals();
    }
}
