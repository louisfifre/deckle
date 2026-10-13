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

public static partial class HudComposition
{
    // Painted per pixel, not as WedgeCount triangles: the antialiased
    // triangle seams meeting at the centre add up to a visible radial moiré.
    private static CompositionDrawingSurface PaintConicSurface(
        CanvasDevice canvasDevice,
        CompositionGraphicsDevice graphicsDevice,
        int pxSquare,
        ConicArcStrokeConfig cfg)
    {
        var surface = graphicsDevice.CreateDrawingSurface(
            new Windows.Foundation.Size(pxSquare, pxSquare),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);

        var bytes       = new byte[pxSquare * pxSquare * 4];
        float centre    = pxSquare / 2f;
        int   wedges    = Math.Max(1, cfg.WedgeCount);
        float wedgeStep = MathF.Tau / wedges;

        var palette = new byte[wedges * 3];
        for (int i = 0; i < wedges; i++)
        {
            float centreTurns = (i + 0.5f) / wedges;
            float hue = cfg.HueStart + centreTurns * cfg.HueRange;
            var c = ColorSpace.OklchToRgb(cfg.OklchLightness, cfg.OklchChroma, hue);
            int p = i * 3;
            palette[p + 0] = c.B;
            palette[p + 1] = c.G;
            palette[p + 2] = c.R;
        }

        for (int y = 0; y < pxSquare; y++)
        {
            float dy = y + 0.5f - centre;
            for (int x = 0; x < pxSquare; x++)
            {
                float dx = x + 0.5f - centre;

                float ang = MathF.Atan2(dy, dx);
                if (ang < 0) ang += MathF.Tau;

                int wi = (int)(ang / wedgeStep);
                if (wi >= wedges) wi = wedges - 1;

                int idx = (y * pxSquare + x) * 4;
                int p   = wi * 3;
                bytes[idx + 0] = palette[p + 0];
                bytes[idx + 1] = palette[p + 1];
                bytes[idx + 2] = palette[p + 2];
                bytes[idx + 3] = 0xFF;
            }
        }

        // CreateFromBytes takes Windows.Graphics.DirectX.DirectXPixelFormat,
        // while the unqualified name resolves to the Microsoft.Graphics.DirectX
        // homonym the drawing surface takes.
        using var bitmap = CanvasBitmap.CreateFromBytes(
            canvasDevice, bytes, pxSquare, pxSquare,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        using var ds = CanvasComposition.CreateDrawingSession(surface);
        ds.Clear(Colors.Transparent);
        ds.DrawImage(bitmap);

        return surface;
    }

    // One span in [0, 2π·ConicSpanTurns] with lead and tail alpha ramps,
    // mirrored at +π when ArcMirror is set. Painted per pixel for the same
    // moiré reason as PaintConicSurface.
    //
    // `fillColor` is written premultiplied by the coverage alpha. Masks pass
    // white, since AlphaMaskEffect reads only alpha; the Playground's naked
    // ArcMask view draws the surface directly and passes a theme-legible
    // colour.
    private static CompositionDrawingSurface PaintArcMaskSurface(
        CanvasDevice canvasDevice,
        CompositionGraphicsDevice graphicsDevice,
        int pxSquare,
        ConicArcStrokeConfig cfg,
        Color fillColor)
    {
        var surface = graphicsDevice.CreateDrawingSurface(
            new Windows.Foundation.Size(pxSquare, pxSquare),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);

        // Mirror caps Span at 0.5 so the two arcs cannot overlap.
        float maxSpanTurns  = cfg.ArcMirror ? 0.5f : 1f;
        float spanTurns     = Math.Clamp(cfg.ConicSpanTurns, 0f, maxSpanTurns);
        float leadFadeTurns = Math.Clamp(cfg.ConicLeadFadeTurns, 0f, spanTurns);
        float tailFadeTurns = Math.Clamp(cfg.ConicTailFadeTurns, 0f, spanTurns);
        float totalFadeTurns = leadFadeTurns + tailFadeTurns;
        if (totalFadeTurns > spanTurns && totalFadeTurns > 0f)
        {
            float scale = spanTurns / totalFadeTurns;
            leadFadeTurns *= scale;
            tailFadeTurns *= scale;
        }
        float spanRadians      = MathF.Tau * spanTurns;
        float leadFadeRadians  = MathF.Tau * leadFadeTurns;
        float tailFadeRadians  = MathF.Tau * tailFadeTurns;
        float tailStartRadians = spanRadians - tailFadeRadians;
        float curve            = MathF.Max(0.01f, cfg.ConicFadeCurve);
        bool  mirror           = cfg.ArcMirror;

        if (spanRadians <= 0f)
            return surface;

        var bytes  = new byte[pxSquare * pxSquare * 4];
        float centre = pxSquare / 2f;

        for (int y = 0; y < pxSquare; y++)
        {
            float dy = y + 0.5f - centre;
            for (int x = 0; x < pxSquare; x++)
            {
                float dx = x + 0.5f - centre;

                float ang = MathF.Atan2(dy, dx);
                if (ang < 0) ang += MathF.Tau;

                if (mirror && ang >= MathF.PI)
                    ang -= MathF.PI;

                float alpha;
                if (ang >= spanRadians)
                {
                    alpha = 0f;
                }
                else if (leadFadeRadians > 0f && ang < leadFadeRadians)
                {
                    float t = ang / leadFadeRadians;
                    alpha = MathF.Pow(t, curve);
                }
                else if (tailFadeRadians > 0f && ang >= tailStartRadians)
                {
                    float t = (ang - tailStartRadians) / tailFadeRadians;
                    alpha = MathF.Pow(1f - t, curve);
                }
                else
                {
                    alpha = 1f;
                }

                byte a = (byte)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 255f);
                int idx = (y * pxSquare + x) * 4;

                bytes[idx + 0] = (byte)((fillColor.B * a) / 255);
                bytes[idx + 1] = (byte)((fillColor.G * a) / 255);
                bytes[idx + 2] = (byte)((fillColor.R * a) / 255);
                bytes[idx + 3] = a;
            }
        }

        using var bitmap = CanvasBitmap.CreateFromBytes(
            canvasDevice, bytes, pxSquare, pxSquare,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        using var ds = CanvasComposition.CreateDrawingSession(surface);
        ds.Clear(Colors.Transparent);
        ds.DrawImage(bitmap);

        return surface;
    }
}
