using Deckle.Composition;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

internal static class AmbientZoneSampler
{
    public static int ResolveBandCells(BorderThicknessMode mode, double depthShare, int cellsPerEdge, int axisDim)
    {
        if (axisDim <= 0) return 0;
        int raw = mode switch
        {
            BorderThicknessMode.Share => (int)Math.Round(Math.Clamp(depthShare, 0.05, 0.5) * axisDim),
            BorderThicknessMode.Cells => cellsPerEdge,
            _                         => (int)Math.Round(0.33 * axisDim),
        };
        return Math.Clamp(raw, 1, axisDim);
    }

    // Mean of the zone's band cells in linear light, unrounded. Exactly
    // (0, 0, 0) only when every cell of the band is black.
    public static (float R, float G, float B) SampleZone(SampledFrame sample, LightZone zone, int bandCells)
    {
        int cols = sample.Cols;
        int rows = sample.Rows;

        int cMin, cMax, rMin, rMax;
        switch (zone)
        {
            case LightZone.Top:
                cMin = 0;
                cMax = cols - 1;
                rMin = 0;
                rMax = Math.Clamp(bandCells, 1, rows) - 1;
                break;
            case LightZone.Bottom:
                cMin = 0;
                cMax = cols - 1;
                rMin = rows - Math.Clamp(bandCells, 1, rows);
                rMax = rows - 1;
                break;
            case LightZone.Left:
                cMin = 0;
                cMax = Math.Clamp(bandCells, 1, cols) - 1;
                rMin = 0;
                rMax = rows - 1;
                break;
            case LightZone.Right:
                cMin = cols - Math.Clamp(bandCells, 1, cols);
                cMax = cols - 1;
                rMin = 0;
                rMax = rows - 1;
                break;
            default:
                return (0f, 0f, 0f);
        }

        double sumRLin = 0, sumGLin = 0, sumBLin = 0;
        int count = 0;
        for (int r = rMin; r <= rMax; r++)
        {
            int rowBase = r * cols;
            for (int c = cMin; c <= cMax; c++)
            {
                var px = sample.Grid[rowBase + c];
                sumRLin += ColorSpace.SrgbToLinear8Lut[px.R];
                sumGLin += ColorSpace.SrgbToLinear8Lut[px.G];
                sumBLin += ColorSpace.SrgbToLinear8Lut[px.B];
                count++;
            }
        }
        if (count == 0) return (0f, 0f, 0f);

        return ((float)(sumRLin / count), (float)(sumGLin / count), (float)(sumBLin / count));
    }
}
