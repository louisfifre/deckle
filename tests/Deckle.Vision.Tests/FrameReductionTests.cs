using System.Reflection;
using Deckle.Composition;
using Windows.UI;
using Xunit;

namespace Deckle.Vision.Tests;

// The compute-shader reduction replaces GenerateMips; the grid it publishes
// must mean the same thing, because AmbientZoneSampler and everything
// downstream read it unchanged. Both paths run here on WARP, the software
// rasteriser, over synthetic desktops, and are compared cell by cell.
//
// Stated tolerances (8-bit channels):
//  - against an exact CPU box mean, 1 level: the shader rounds once;
//  - against the legacy mip chain on a size the chain halves evenly, 2
//    levels: the chain rounds to 8 bits at each of its six levels;
//  - against the legacy chain on a real 4K size (2160 rows do not halve to
//    a whole number of cells), 8 levels on smooth content (6 measured on
//    WARP). Hard edges are
//    left out of that case on purpose: the chain's odd levels (135 -> 67 ->
//    33 rows) stretch and blend, so a cell that straddles an edge can
//    differ by tens of levels from the shader's exact bounds. Neither is
//    wrong; the shader's cell is the exact mean of its share of the frame.
[Trait("Category", "integration")]
public sealed class FrameReductionTests
{
    [Fact]
    public void TheShaderBytecodeIsEmbedded()
    {
        using var stream = typeof(FrameSampler).Assembly
            .GetManifestResourceStream("Deckle.Vision.FrameReduce.cso");

        Assert.NotNull(stream);
        var magic = new byte[4];
        Assert.Equal(4, stream.Read(magic, 0, 4));
        Assert.Equal("DXBC", System.Text.Encoding.ASCII.GetString(magic));
    }

    [Fact]
    public async Task TheShaderMatchesAnExactBoxMeanOnAnEvenSize()
    {
        const int width = 3840, height = 2112;
        byte[] pixels = SdrDesktop(width, height);
        using var rig = new WarpRig();
        nint texture = rig.CreateTexture(width, height, hdr: false, pixels);

        var sample = await Sample(rig, width, height, hdr: false, texture, FrameSamplerPath.GpuReduction);

        Assert.Equal((60, 33), (sample.Cols, sample.Rows));
        int worst = WorstSdrDifference(sample, ExpectedSdrGrid(pixels, width, height, sample.Cols, sample.Rows));
        Assert.True(worst <= 1, $"worst channel difference {worst} levels");
    }

    [Fact]
    public async Task TheShaderMatchesTheMipChainOnAnEvenSize()
    {
        const int width = 3840, height = 2112;
        using var rig = new WarpRig();
        nint texture = rig.CreateTexture(width, height, hdr: false, SdrDesktop(width, height));

        var legacy = await Sample(rig, width, height, hdr: false, texture, FrameSamplerPath.Legacy);
        var reduced = await Sample(rig, width, height, hdr: false, texture, FrameSamplerPath.GpuReduction);

        int worst = WorstSdrDifference(reduced, legacy.Grid);
        Assert.True(worst <= 2, $"worst channel difference {worst} levels");
    }

    [Fact]
    public async Task TheShaderMatchesTheMipChainOnA4KDesktop()
    {
        const int width = 3840, height = 2160;
        using var rig = new WarpRig();
        nint texture = rig.CreateTexture(width, height, hdr: false, SdrDesktop(width, height, withBlock: false));

        var legacy = await Sample(rig, width, height, hdr: false, texture, FrameSamplerPath.Legacy);
        var reduced = await Sample(rig, width, height, hdr: false, texture, FrameSamplerPath.GpuReduction);

        Assert.Equal((legacy.Cols, legacy.Rows), (reduced.Cols, reduced.Rows));
        int worst = WorstSdrDifference(reduced, legacy.Grid);
        Assert.True(worst <= 8, $"worst channel difference {worst} levels");
    }

    [Fact]
    public async Task TheShaderKeepsTheLinearAverageOfTheMipChain()
    {
        const int width = 3840, height = 2160;
        using var rig = new WarpRig();
        nint texture = rig.CreateTexture(width, height, hdr: false, SdrDesktop(width, height, withBlock: false));

        var legacy = await Sample(rig, width, height, hdr: false, texture, FrameSamplerPath.Legacy);
        var reduced = await Sample(rig, width, height, hdr: false, texture, FrameSamplerPath.GpuReduction);

        // 8 % relative: on WARP the 4K mip chain reads the vertical gradient
        // about 2 % compressed (it behaves as if 48 of the 2160 rows were
        // cropped), which moves the linear mean by about 5 %. Not seen on a
        // real GPU, whose GenerateMips may differ.
        AssertClose(legacy.AverageLinear.R, reduced.AverageLinear.R, 0.08f);
        AssertClose(legacy.AverageLinear.G, reduced.AverageLinear.G, 0.08f);
        AssertClose(legacy.AverageLinear.B, reduced.AverageLinear.B, 0.08f);
    }

    [Fact]
    public async Task TheShaderMatchesTheMipChainOnAnHdrDesktop()
    {
        const int width = 1920, height = 1056;
        using var rig = new WarpRig();
        nint texture = rig.CreateTexture(width, height, hdr: true, HdrDesktop(width, height));

        var legacy = await Sample(rig, width, height, hdr: true, texture, FrameSamplerPath.Legacy);
        var reduced = await Sample(rig, width, height, hdr: true, texture, FrameSamplerPath.GpuReduction);

        Assert.Equal((60, 33), (reduced.Cols, reduced.Rows));
        int worst = WorstSdrDifference(reduced, legacy.Grid);
        Assert.True(worst <= 2, $"worst channel difference {worst} levels");
        AssertClose(legacy.AverageLinear.R, reduced.AverageLinear.R, 0.02f);
    }

    [Fact]
    public async Task ABlackFrameStaysExactlyBlack()
    {
        // The engine's black gate relies on an exact (0, 0, 0) average.
        const int width = 1920, height = 1056;
        using var rig = new WarpRig();
        nint texture = rig.CreateTexture(width, height, hdr: false, new byte[width * height * 4]);

        var sample = await Sample(rig, width, height, hdr: false, texture, FrameSamplerPath.GpuReduction);

        Assert.Equal((0f, 0f, 0f), sample.AverageLinear);
    }

    [Fact]
    public async Task TheShaderPathIsTheDefaultAndCanBeForcedBackToLegacy()
    {
        using var rig = new WarpRig();
        await using var forced = rig.CreateSampler(1920, 1056, hdr: false, FrameSamplerPath.Legacy);
        await using var normal = rig.CreateSampler(1920, 1056, hdr: false, FrameSamplerPath.GpuReduction);

        Assert.Equal(FrameSamplerPath.Legacy, forced.ActivePath);
        Assert.Equal(FrameSamplerPath.GpuReduction, normal.ActivePath);
    }

    // ── Synthetic desktops ───────────────────────────────────────────────

    // Smooth gradients on every channel, plus a hard-edged bright block whose
    // edges fall on grid-cell borders when the height is a multiple of 33.
    private static byte[] SdrDesktop(int width, int height, bool withBlock = true)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                byte b = (byte)(x * 255L / (width - 1));
                byte g = (byte)(y * 255L / (height - 1));
                byte r = (byte)(255 - (x + y) * 255L / (width + height - 2));
                bool block = withBlock && x >= width / 4 && x < width / 2 && y >= height / 3 && y < height * 2 / 3;
                pixels[i + 0] = block ? (byte)240 : b;
                pixels[i + 1] = block ? (byte)200 : g;
                pixels[i + 2] = block ? (byte)30 : r;
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    // scRGB values from 0 up to 3.0 (240 nits), the range above 1.0 that the
    // tone-map exists for.
    private static byte[] HdrDesktop(int width, int height)
    {
        var pixels = new byte[width * height * 8];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 8;
                Write(pixels, i + 0, 3f * x / (width - 1));
                Write(pixels, i + 2, 3f * y / (height - 1));
                Write(pixels, i + 4, 1.5f * (x + y) / (width + height - 2));
                Write(pixels, i + 6, 1f);
            }
        }
        return pixels;

        static void Write(byte[] buffer, int offset, float value)
            => BitConverter.TryWriteBytes(buffer.AsSpan(offset, 2), (Half)value);
    }

    // ── Running a path and comparing ─────────────────────────────────────

    private static async Task<SampledFrame> Sample(
        WarpRig rig, int width, int height, bool hdr, nint texture, FrameSamplerPath path)
    {
        await using var sampler = rig.CreateSampler(width, height, hdr, path);
        Assert.Equal(path, sampler.ActivePath);

        var frame = new CapturedFrame(texture, width, height, timestampTicks: 1);
        Assert.True(sampler.Process(frame));
        return sampler.LatestSample ?? throw new Xunit.Sdk.XunitException("no sample was published");
    }

    private static void AssertClose(float expected, float actual, float relativeTolerance)
        => Assert.True(
            Math.Abs(actual - expected) <= expected * relativeTolerance,
            $"expected {expected} within {relativeTolerance:P0}, got {actual}");

    private static int WorstSdrDifference(SampledFrame actual, Color[] expected)
    {
        Assert.Equal(expected.Length, actual.Grid.Length);
        int worst = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(actual.Grid[i].R - expected[i].R));
            worst = Math.Max(worst, Math.Abs(actual.Grid[i].G - expected[i].G));
            worst = Math.Max(worst, Math.Abs(actual.Grid[i].B - expected[i].B));
        }
        return worst;
    }

    // The exact mean of each cell's source block, rounded once, computed on
    // the CPU with the same cell bounds as the shader.
    private static Color[] ExpectedSdrGrid(byte[] pixels, int width, int height, int cols, int rows)
    {
        var grid = new Color[cols * rows];
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                int x0 = cx * width / cols, x1 = (cx + 1) * width / cols;
                int y0 = cy * height / rows, y1 = (cy + 1) * height / rows;
                long sumB = 0, sumG = 0, sumR = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int i = (y * width + x) * 4;
                        sumB += pixels[i];
                        sumG += pixels[i + 1];
                        sumR += pixels[i + 2];
                    }
                }
                double n = (x1 - x0) * (double)(y1 - y0);
                grid[cy * cols + cx] = Color.FromArgb(
                    255,
                    (byte)Math.Round(sumR / n),
                    (byte)Math.Round(sumG / n),
                    (byte)Math.Round(sumB / n));
            }
        }
        return grid;
    }
}
