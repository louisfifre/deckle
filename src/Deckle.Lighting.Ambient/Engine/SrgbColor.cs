using Deckle.Composition;
using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

// The colour the ambient path works on between the sampler and the push :
// three sRGB-encoded channels in [0, 1], kept in float so no stage rounds
// to 8 bits. Encoded (not linear) because the brightness curve, the floor
// and the smoothing are defined on encoded values (an encoded 1.0 is the
// byte 255). Quantize is the single rounding to the 8-bit LightColor the
// transports take.
internal readonly record struct SrgbColor(float R, float G, float B)
{
    public static SrgbColor Black => default;

    public float Max => MathF.Max(R, MathF.Max(G, B));

    public bool IsBlack => R == 0f && G == 0f && B == 0f;

    public static SrgbColor FromLinear(float r, float g, float b)
        => new(Encode(r), Encode(g), Encode(b));

    public static SrgbColor FromLinear((float R, float G, float B) linear)
        => FromLinear(linear.R, linear.G, linear.B);

    public (float R, float G, float B) ToLinear()
        => (ColorSpace.SrgbToLinear(R), ColorSpace.SrgbToLinear(G), ColorSpace.SrgbToLinear(B));

    public SrgbColor Scale(float factor)
        => new(Math.Clamp(R * factor, 0f, 1f), Math.Clamp(G * factor, 0f, 1f), Math.Clamp(B * factor, 0f, 1f));

    public LightColor Quantize()
        => new(ToByte(R), ToByte(G), ToByte(B));

    private static float Encode(float linear)
        => Math.Clamp(ColorSpace.LinearToSrgb(linear), 0f, 1f);

    private static byte ToByte(float encoded)
        => (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
}
