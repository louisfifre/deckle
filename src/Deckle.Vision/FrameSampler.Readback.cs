using Windows.UI;
using Deckle.Composition;

namespace Deckle.Vision;

public sealed partial class FrameSampler
{
    private SampledFrame ReadSampleFromMapped(in ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE mapped)
    {
        var grid = new Color[_gridCols * _gridRows];
        double sumRLin = 0, sumGLin = 0, sumBLin = 0;
        int count = 0;

        if (_isHdr)
        {
            ReadGridFP16(in mapped, grid, ref sumRLin, ref sumGLin, ref sumBLin, ref count);
        }
        else
        {
            ReadGridBGRA8(in mapped, grid, ref sumRLin, ref sumGLin, ref sumBLin, ref count);
        }

        // Average in linear light, re-encode via the sRGB OETF so the
        // returned colour matches what a perceptually-correct mean of
        // the source pixels would produce. Arithmetic averaging on the
        // raw sRGB bytes would bias mid-tones upward (sRGB's ~2.4 gamma
        // makes equal byte deltas non-uniform in photons).
        Color avg;
        if (count == 0)
        {
            avg = Color.FromArgb(0xFF, 0, 0, 0);
        }
        else
        {
            float avgR = (float)(sumRLin / count);
            float avgG = (float)(sumGLin / count);
            float avgB = (float)(sumBLin / count);
            avg = Color.FromArgb(0xFF,
                (byte)Math.Clamp((int)MathF.Round(ColorSpace.LinearToSrgb(avgR) * 255f), 0, 255),
                (byte)Math.Clamp((int)MathF.Round(ColorSpace.LinearToSrgb(avgG) * 255f), 0, 255),
                (byte)Math.Clamp((int)MathF.Round(ColorSpace.LinearToSrgb(avgB) * 255f), 0, 255));
        }

        return new SampledFrame(avg, grid, _gridCols, _gridRows);
    }

    private void ReadGridBGRA8(
        in ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE mapped,
        Color[] grid,
        ref double sumRLin, ref double sumGLin, ref double sumBLin, ref int count)
    {
        // Exposure compensation in linear light, mirroring ReadGridFP16
        // semantics. Without this branch the SDR path used to ignore
        // _exposureEv entirely — the AmbientPage slider had visible
        // effect only on HDR displays. When _exposureEv = 0 the
        // multiplier collapses to 1.0 and the inner loop is
        // arithmetically equivalent to the legacy direct LUT lookup.
        double exposureMultiplier = Math.Pow(2.0, _exposureEv);
        bool exposed = _exposureEv != 0.0;

        unsafe
        {
            byte* basePtr = (byte*)mapped.pData;
            int rowPitch = (int)mapped.RowPitch;

            for (int row = 0; row < _gridRows; row++)
            {
                byte* rowPtr = basePtr + row * rowPitch;
                for (int col = 0; col < _gridCols; col++)
                {
                    byte* p = rowPtr + col * 4;
                    byte b = p[0];
                    byte g = p[1];
                    byte r = p[2];

                    if (exposed)
                    {
                        // Scale in linear light, then re-encode sRGB
                        // for the per-cell display and feed the sum
                        // with the post-exposure linear value so the
                        // engine's downstream averaging respects the
                        // user's EV choice.
                        float rLin = (float)(ColorSpace.SrgbToLinear8Lut[r] * exposureMultiplier);
                        float gLin = (float)(ColorSpace.SrgbToLinear8Lut[g] * exposureMultiplier);
                        float bLin = (float)(ColorSpace.SrgbToLinear8Lut[b] * exposureMultiplier);

                        byte rOut = (byte)Math.Clamp((int)MathF.Round(ColorSpace.LinearToSrgb(rLin) * 255f), 0, 255);
                        byte gOut = (byte)Math.Clamp((int)MathF.Round(ColorSpace.LinearToSrgb(gLin) * 255f), 0, 255);
                        byte bOut = (byte)Math.Clamp((int)MathF.Round(ColorSpace.LinearToSrgb(bLin) * 255f), 0, 255);

                        grid[row * _gridCols + col] = Color.FromArgb(0xFF, rOut, gOut, bOut);
                        sumRLin += rLin;
                        sumGLin += gLin;
                        sumBLin += bLin;
                    }
                    else
                    {
                        grid[row * _gridCols + col] = Color.FromArgb(0xFF, r, g, b);
                        sumRLin += ColorSpace.SrgbToLinear8Lut[r];
                        sumGLin += ColorSpace.SrgbToLinear8Lut[g];
                        sumBLin += ColorSpace.SrgbToLinear8Lut[b];
                    }
                    count++;
                }
            }
        }
    }

    private void ReadGridFP16(
        in ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE mapped,
        Color[] grid,
        ref double sumRLin, ref double sumGLin, ref double sumBLin, ref int count)
    {
        // Tone-map runs against the rolling content peak captured by
        // the *previous* frame ; this frame's max feeds the rolling
        // update at the end of the loop. One-frame lag at 15 Hz is
        // imperceptible and avoids a two-pass read.
        float framePeak = 1f; // SDR floor — never normalise below 1.0
        float toneMapPeak = _contentPeak;
        double exposureEv = _exposureEv;

        unsafe
        {
            byte* basePtr = (byte*)mapped.pData;
            int rowPitch = (int)mapped.RowPitch;

            for (int row = 0; row < _gridRows; row++)
            {
                ushort* rowPtr = (ushort*)(basePtr + row * rowPitch);
                for (int col = 0; col < _gridCols; col++)
                {
                    ushort* p = rowPtr + col * 4;
                    float r = (float)BitConverter.UInt16BitsToHalf(p[0]);
                    float g = (float)BitConverter.UInt16BitsToHalf(p[1]);
                    float b = (float)BitConverter.UInt16BitsToHalf(p[2]);

                    if (r > framePeak) framePeak = r;
                    if (g > framePeak) framePeak = g;
                    if (b > framePeak) framePeak = b;

                    // Per-pixel Hable tone-map in linear scRGB, then
                    // sRGB OETF for the per-cell display value. The
                    // sum that feeds the grid-wide average goes back
                    // through the LUT so it stays in linear light
                    // (symmetric with ReadGridBGRA8 — the bias
                    // introduced by re-encoding then re-linearising
                    // is sub-perceptual after Hable has already
                    // compressed the highlights).
                    Color c = ColorSpace.ScRgbToSrgb(r, g, b, toneMapPeak, exposureEv);
                    grid[row * _gridCols + col] = c;
                    sumRLin += ColorSpace.SrgbToLinear8Lut[c.R];
                    sumGLin += ColorSpace.SrgbToLinear8Lut[c.G];
                    sumBLin += ColorSpace.SrgbToLinear8Lut[c.B];
                    count++;
                }
            }
        }

        // Rolling content peak update. Attack is instant (peak rises
        // with the first bright frame) ; release decays toward the
        // current frame's max so a quick scene change brightens fast
        // but doesn't crash when a dark frame slips in. Capped at the
        // display's hard ceiling — a freak sun-glint pixel reading
        // above peakWhite (rare but possible in scRGB) cannot crush
        // the rest of the scene below it.
        if (framePeak > _contentPeak)
        {
            _contentPeak = framePeak;
        }
        else
        {
            _contentPeak = _contentPeak * ContentPeakReleaseDecay
                         + framePeak * (1f - ContentPeakReleaseDecay);
        }
        if (_contentPeak > _displayPeakScRgb) _contentPeak = _displayPeakScRgb;
        if (_contentPeak < 1f) _contentPeak = 1f; // SDR floor
    }

}
