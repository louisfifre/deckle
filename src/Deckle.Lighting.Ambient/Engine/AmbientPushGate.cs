using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

internal static class AmbientPushGate
{
    public static bool ShouldDrop(
        LightColor target,
        (int R, int G, int B) previous,
        int threshold,
        bool requiresContinuousColorUpdates)
    {
        if (requiresContinuousColorUpdates || previous.R < 0)
            return false;

        int delta = Math.Abs(target.R - previous.R)
                  + Math.Abs(target.G - previous.G)
                  + Math.Abs(target.B - previous.B);
        return delta < threshold;
    }
}
