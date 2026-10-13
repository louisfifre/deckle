using Windows.UI;
using Deckle.Composition;

namespace Deckle.Vision;

public sealed partial class FrameSampler
{
    private SampledFrame ReadSampleFromMapped(
        in ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE mapped, double elapsedSeconds)
    {
        var grid = new Color[_gridCols * _gridRows];
        double sumRLin = 0, sumGLin = 0, sumBLin = 0;
        int count = 0;

        if (_isHdr)
        {
            ReadGridFP16(in mapped, grid, elapsedSeconds, ref sumRLin, ref sumGLin, ref sumBLin, ref count);
        }
        else
        {
            ReadGridBGRA8(in mapped, grid, ref sumRLin, ref sumGLin, ref sumBLin, ref count);
        }

        // Average in linear light, then re-encode to sRGB: averaging the
        // raw sRGB bytes would darken mixed cells, because sRGB is not
        // linear in light. The linear mean is also published unrounded:
        // the engine tunes it in float and quantises once at its output.
        Color avg;
        (float R, float G, float B) avgLinear;
        if (count == 0)
        {
            avg = Color.FromArgb(0xFF, 0, 0, 0);
            avgLinear = (0f, 0f, 0f);
        }
        else
        {
            float avgR = (float)(sumRLin / count);
            float avgG = (float)(sumGLin / count);
            float avgB = (float)(sumBLin / count);
            avgLinear = (avgR, avgG, avgB);
            avg = Color.FromArgb(0xFF,
                (byte)Math.Clamp((int)MathF.Round(ColorSpace.LinearToSrgb(avgR) * 255f), 0, 255),
                (byte)Math.Clamp((int)MathF.Round(ColorSpace.LinearToSrgb(avgG) * 255f), 0, 255),
                (byte)Math.Clamp((int)MathF.Round(ColorSpace.LinearToSrgb(avgB) * 255f), 0, 255));
        }

        return new SampledFrame(avg, grid, _gridCols, _gridRows, avgLinear);
    }

    private void ReadGridBGRA8(
        in ScreenCaptureInterop.D3D11_MAPPED_SUBRESOURCE mapped,
        Color[] grid,
        ref double sumRLin, ref double sumGLin, ref double sumBLin, ref int count)
    {
        // Exposure scales in linear light, as on the HDR path. At 0 EV the
        // plain branch gives the same result without the re-encode.
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
        double elapsedSeconds,
        ref double sumRLin, ref double sumGLin, ref double sumBLin, ref int count)
    {
        // The tone-map uses the content peak from the previous frame; this
        // frame's max updates it after the loop. The one-frame lag avoids a
        // second pass over the grid.
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

                    // The sum re-linearises the tone-mapped sRGB cell so
                    // the average stays in linear light, as on the SDR
                    // path; the 8-bit round trip is negligible once Hable
                    // has compressed the highlights.
                    Color c = ColorSpace.ScRgbToSrgb(r, g, b, toneMapPeak, exposureEv);
                    grid[row * _gridCols + col] = c;
                    sumRLin += ColorSpace.SrgbToLinear8Lut[c.R];
                    sumGLin += ColorSpace.SrgbToLinear8Lut[c.G];
                    sumBLin += ColorSpace.SrgbToLinear8Lut[c.B];
                    count++;
                }
            }
        }

        // Instant attack, so a bright cut lands at once; the release decays
        // toward this frame's max over the time since the previous processed
        // frame, so the peak falls at the same speed whatever the capture
        // rate and one dark frame does not crash it.
        if (framePeak > _contentPeak)
        {
            _contentPeak = framePeak;
        }
        else
        {
            float retention = ContentPeakRetention(elapsedSeconds);
            _contentPeak = _contentPeak * retention
                         + framePeak * (1f - retention);
        }
        if (_contentPeak > _displayPeakScRgb) _contentPeak = _displayPeakScRgb;
        if (_contentPeak < 1f) _contentPeak = 1f; // SDR floor
    }

}
