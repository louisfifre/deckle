namespace Deckle.Lighting.Hue;

public readonly record struct HueProjectedState(
    bool On,
    int? Brightness,
    (float X, float Y)? Xy);

public static class HueStateProjection
{
    public static HueProjectedState FromLightColor(LightColor color)
    {
        var (xy, bri) = HueColorMath.RgbToHueXyBri(color);

        if (bri == 0)
        {
            return new HueProjectedState(
                On: false,
                Brightness: null,
                Xy: null);
        }

        return new HueProjectedState(
            On: true,
            Brightness: (int)Math.Round(bri * 100.0 / 254.0),
            Xy: ((float)xy.X, (float)xy.Y));
    }
}
