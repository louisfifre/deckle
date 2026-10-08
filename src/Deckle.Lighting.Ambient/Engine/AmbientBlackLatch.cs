using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

// The single quantisation to 8 bits, with a black latch per key ("group"
// or a light id). Plain rounding sends any max channel under half a step
// (0.5/255) to black : under a shadow-crushing curve such as the default
// (0.42, 0, 1, 1), a steady dark but non-black frame (8/255 → ~0.46/255)
// turned the lamp off. The latch splits that threshold in two :
//
//   - a lit key goes black only when its max channel falls below
//     EnterBlack (0.25/255) ;
//   - a black key lights again only when its max channel rises above
//     LeaveBlack (0.75/255) ;
//   - while lit, a value that would round to black is lifted so its max
//     channel is one step (1/255).
//
// A key seen for the first time starts lit unless it is already under
// EnterBlack. An exactly black source smooths down to 0 and always ends
// black. With the floor on, values never come near this band.
//
// Not thread-safe : the push loop is its only caller.
internal sealed class AmbientBlackLatch
{
    internal const float EnterBlack = 0.25f / 255f;
    internal const float LeaveBlack = 0.75f / 255f;
    private const float OneStep = 1f / 255f;

    private readonly Dictionary<string, bool> _lit = new();

    public LightColor Quantize(string key, SrgbColor color)
    {
        float max = color.Max;
        bool lit = _lit.TryGetValue(key, out bool wasLit)
            ? wasLit ? max >= EnterBlack : max > LeaveBlack
            : max >= EnterBlack;
        _lit[key] = lit;

        if (!lit) return LightColor.Black;

        LightColor quantized = color.Quantize();
        return quantized == LightColor.Black
            ? color.Scale(OneStep / max).Quantize()
            : quantized;
    }

    public void Reset(string key) => _lit.Remove(key);

    public void Clear() => _lit.Clear();
}
