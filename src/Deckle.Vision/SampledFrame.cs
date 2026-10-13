using Windows.UI;

namespace Deckle.Vision;

// One snapshot from FrameSampler. AverageLinear is the grid mean in linear
// light, unrounded: what AmbientEngine tunes and pushes to the lamps, and
// exactly (0, 0, 0) only when every cell is black (the engine's black gate
// relies on it). Average is the same mean in sRGB bytes, for previews. Grid
// is row-major, Grid[row * Cols + col], and is never mutated after
// publication.
public sealed record SampledFrame(
    Color Average,
    Color[] Grid,
    int Cols,
    int Rows,
    (float R, float G, float B) AverageLinear);
