using Windows.UI;

namespace Deckle.Vision;

// One snapshot from FrameSampler. AverageLinear is the mean over the
// whole grid in linear light, unrounded (what AmbientEngine tunes and
// pushes to the lamps ; exactly (0, 0, 0) only when every cell is
// black). Average is the same mean re-encoded to sRGB bytes, for
// previews. Grid is laid out row-major — Grid[row * Cols + col]. All
// reflect the same source frame and are produced atomically inside
// FrameSampler.Process ; a new SampledFrame is allocated per processed frame and replaces
// LatestSample via a single volatile write. Consumers read it via
// volatile read and use it directly — the array is never mutated after
// publication.
public sealed record SampledFrame(
    Color Average,
    Color[] Grid,
    int Cols,
    int Rows,
    (float R, float G, float B) AverageLinear);
