using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Windows.UI;

namespace Deckle.Composition;

// HUD Composition pipeline: processing strokes for the chrono surface.
//
// Every visual stays inside the 272x78 HUD rect. The HUD window is
// WS_EX_LAYERED for the proximity fade, which disables the DWM shell shadow,
// and the rect fits the card tightly, so an external DropShadow would clip at
// the HWND edge into a rectangular artifact. No shadows in this pipeline.
//
// The stroke straddles the HWND edge (negative InsetDip), so only its inner
// half shows. CornerRadiusDip follows the window's DWMWCP_ROUND corner.
public static partial class HudComposition
{
    // ╔════════════════════════════════════════════════════════════════════╗
    // ║  Shared geometry                                                   ║
    // ╚════════════════════════════════════════════════════════════════════╝
    private const float  CornerRadiusDip               = 8f;   // dip, rounded-rect corner radius

}
