using Microsoft.UI.Xaml;
using Deckle.Core;

namespace Deckle.Hud;

// TranscriptPanelWindow — cursor proximity, inverted from the HUD's: the panel
// is near-invisible away from the cursor and becomes opaque under it, so it
// stays out of the way until the user looks for it.
public sealed partial class TranscriptPanelWindow : Window
{
    // Same radii as HudWindow; the endpoint alphas are swapped.
    private const double NEAR_RADIUS_DIP = 10;
    private const double FAR_RADIUS_DIP  = 128;
    private const byte   NEAR_ALPHA      = 255; // opaque under the cursor
    private const byte   FAR_ALPHA       = 40;  // near-invisible away from it

    // Cached method-group delegate: subscribing a fresh one on every show
    // would allocate, and the same instance is needed to unsubscribe.
    private readonly Action _onCursorMoved;
    private bool _proximityActive;
    private byte _proximityAlpha = FAR_ALPHA;

    private void BeginProximity()
    {
        if (_proximityActive || !_shown) return;
        _proximityActive = true;
        _cursorSignal.Moved += _onCursorMoved;
        UpdateProximity();
    }

    private void EndProximity()
    {
        if (!_proximityActive) return;
        _proximityActive = false;
        _cursorSignal.Moved -= _onCursorMoved;
    }

    // ~125 Hz on mouse move: allocation-free, writes the alpha only when it
    // changes.
    private void UpdateProximity()
    {
        byte alpha = ComputeProximityAlpha();
        if (alpha == _proximityAlpha) return;
        _proximityAlpha = alpha;
        _fade.FadeTo(alpha, instant: true);
    }

    private byte ComputeProximityAlpha()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return _proximityAlpha;

        var pos  = AppWindow.Position;
        var size = AppWindow.Size;
        double distancePx = ProximityFade.DistanceToRectPx(
            cursor.X, cursor.Y,
            pos.X, pos.Y, pos.X + size.Width, pos.Y + size.Height);

        return ProximityFade.Alpha(
            distancePx, _dpiScale, NEAR_RADIUS_DIP, FAR_RADIUS_DIP, NEAR_ALPHA, FAR_ALPHA);
    }
}
