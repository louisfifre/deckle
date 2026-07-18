using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Hud;

// HudChrono — static digit reveal for Transcribing and Rewriting.
//
// The retired left-to-right swipe has no live path. Each of the six glyphs is
// fully open onto one shared clone of the processing material for the whole
// state. The material may rotate when functional HUD animation is on; its
// opacity never interpolates.
public sealed partial class HudChrono
{
    private const int DigitCount = 6;

    private TextBlock[]? _digitPrimary;
    private TextBlock[]? _digitAccent;
    private bool _revealsActive;

    private void EnsureRevealInfrastructure()
    {
        if (_digitPrimary is not null) return;

        _digitPrimary = new[] { Min1, Min2, Sec1, Sec2, Cs1, Cs2 };
        _digitAccent  = new[] { Min1Accent, Min2Accent, Sec1Accent, Sec2Accent, Cs1Accent, Cs2Accent };
        _cellElements = new FrameworkElement[] { Min1Cell, Min2Cell, Sec1Cell, Sec2Cell, Cs1Cell, Cs2Cell };
    }

    private void StartReveal()
    {
        EnsureRevealInfrastructure();
        if (!_revealsActive)
        {
            _revealsActive = true;
            _revealsFailed = false;
            _revealBuildAttempts = 0;
            EnsureReveals();
        }

        UpdateReveals();
    }

    private void StopReveal()
    {
        if (!_revealsActive) return;

        _revealsActive = false;
        TearDownReveals();
        ClearDigitHeat();
    }

    private void UpdateReveals()
    {
        if (!_revealsActive || _digitPrimary is null || _digitAccent is null) return;

        if (!_revealsFailed && RevealsPending())
            EnsureReveals();

        for (int i = 0; i < DigitCount; i++)
        {
            var reveal = _reveals[i];
            if (reveal is not null)
            {
                reveal.SetHeat(1f);
                _digitAccent[i].Opacity = 0;
            }
            else
            {
                // Flat accent is the permanent fallback if glyph-mask
                // construction fails; it carries the same complete state.
                _digitAccent[i].Opacity = 1;
            }

            _digitPrimary[i].Opacity = 1;
        }

        // Once every mask exists (or the retry window definitively failed),
        // the stopped chrono needs no managed vsync work.
        if (!RevealsPending() || _revealsFailed)
            UnhookRendering();
    }

    private void ClearDigitHeat()
    {
        if (_digitAccent is not null)
            foreach (var digit in _digitAccent) digit.Opacity = 0;

        if (_digitPrimary is not null)
            foreach (var digit in _digitPrimary) digit.Opacity = 1;
    }
}
