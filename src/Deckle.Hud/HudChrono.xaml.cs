using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Deckle.Composition;

namespace Deckle.Hud;

// Chrono card — container + clock + processing stroke attach.
//
// Owns the Bitcount Single MM.SS.cc clock and the progressive digit accent
// (each digit that ever changed locks to SystemFillColorCriticalBrush until
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
//   - HudChrono.Reveal.cs           — the progressive-coloring swipe wave.
//   - HudChrono.Stroke.cs           — the Composition processing stroke.
public sealed partial class HudChrono : UserControl
{
    private bool _renderingHooked;

    private HudState _state = HudState.Hidden;

    public HudChrono()
    {
        InitializeComponent();

        // Pre-init des tableaux _digitPrimary / _digitAccent dès le ctor.
        // Sans ça, ClearDigitHeat() (appelé par ApplyCharging au cold boot
        // pendant le chargement modèle) early-return sur son null check et
        // ne reset pas les opacités → rendu blanc/vide au lieu de "00.00.00"
        // en couleur tertiaire (régression introduite par 7707f09
        // "fix(hud): complementary digit opacities", qui ajoute les accès
        // sans garantir l'init préalable). EnsureSwipeInfra est idempotent
        // (guard `if (_digitPrimary is null)`), donc l'appel ici n'a aucun
        // coût quand StartSwipe() le rappelle.
        EnsureSwipeInfra();

        ChronoRoot.ActualThemeChanged += (_, _) =>
        {
            // Accent TextBlocks bind Foreground via {ThemeResource …} in
            // XAML, so they re-resolve on theme change automatically. The
            // primary TextBlocks inherit from the shared style's
            // ThemeResource Foreground — same story. No per-TextBlock
            // re-assignment needed here (unlike the old design which
            // mutated Foreground in code, which breaks the ThemeResource
            // binding and requires a manual re-push on theme change).

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
        StopSwipe();

        // Pure paint: the "parked" clock look — primary text in the neutral /
        // tertiary colour, waiting for the first recording. Override the Style
        // default Foreground for the 6 digits; dots stay primary (structural
        // punctuation, not data, so they read at full contrast regardless of
        // state). The clock value and the digit glyphs are owned by the clock
        // lifecycle (ResetClock on entry, driven by the host) — Charging must
        // not touch them, so this same style can later be reused over a frozen
        // clock without zeroing it.
        var neutral = ResolveNeutralBrush();
        Min1.Foreground = neutral; Min2.Foreground = neutral;
        Sec1.Foreground = neutral; Sec2.Foreground = neutral;
        Cs1.Foreground  = neutral; Cs2.Foreground  = neutral;
        DotA.Foreground = neutral; DotB.Foreground = neutral;

        DetachProcessingVisual();
    }

    private void ApplyRecording()
    {
        StopSwipe();

        AttachProcessingVisual(ProcessingVariant.Recording);

        // Clear local Foreground so each primary TextBlock inherits its Style
        // default (TextFillColorPrimaryBrush, theme-resource-bound). The clock
        // face itself (the 00.00.00 reset + the ticking + the per-tick accent
        // flash) is owned by StartClock, which the host calls before this.
        ClearDigitForegrounds();
    }

    private void ApplyTranscribing()
    {
        // The clock is frozen by StopClock (the host calls it before this),
        // which also latches the final elapsed value and may light the
        // last-changed digit — we KEEP the animator's changed flags so the
        // swipe knows which digits are eligible for the accent reveal.
        ClearDigitForegrounds();

        AttachProcessingVisual(ProcessingVariant.Transcribing);
        StartSwipe();
        // HookRendering drives OnRendering → UpdateSwipe (the clock is stopped,
        // so UpdateClock is a no-op on the digit values).
        HookRendering();
    }

    private void ApplyRewriting()
    {
        // Clock already frozen by the Transcribing transition; Rewriting only
        // re-skins the stroke and restarts the reveal.
        ClearDigitForegrounds();

        AttachProcessingVisual(ProcessingVariant.Rewriting);
        StartSwipe();
        HookRendering();
    }

    private void ApplyHidden()
    {
        UnhookRendering();
        StopSwipe();

        DetachProcessingVisual();
        // The clock is left as-is (stopped after a take); the next session's
        // ResetClock zeroes it. Rendering is unhooked, so nothing reads a
        // residual value while hidden — it stays invisible and harmless.
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

    // Single vsync dispatcher for both the clock ticker (Recording) and the
    // swipe reveal (Transcribing / Rewriting). UpdateClock early-outs via
    // the stopwatch state when not Recording, so calling both is cheap.
    private void OnRendering(object? sender, object e)
    {
        UpdateClock();
        UpdateSwipe();
    }
}
