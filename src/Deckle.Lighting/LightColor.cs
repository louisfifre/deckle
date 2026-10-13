namespace Deckle.Lighting;

// Generic 24-bit sRGB colour used as the in-app currency between the
// ambient-lighting pipeline and the light-output drivers. Kept deliberately
// thin : no alpha, no colour-space metadata, no gamma flag. Perceptual work
// happens upstream; each driver converts sRGB to its native representation
// (CIE xy + brightness for Hue).
public readonly record struct LightColor(byte R, byte G, byte B)
{
    public static LightColor Black => new(0, 0, 0);
    public static LightColor White => new(255, 255, 255);
    public static LightColor Red   => new(255, 0,   0);
    public static LightColor Green => new(0,   255, 0);
    public static LightColor Blue  => new(0,   0,   255);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
