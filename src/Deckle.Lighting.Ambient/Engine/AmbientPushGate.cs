using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

// What a tick sends to one light (or the group) once its colour is
// quantised.
internal enum AmbientPushDecision
{
    // Send the new colour.
    PushNew,

    // Send the previous pushed colour again : the change is under the
    // threshold, but the output needs a colour on every tick.
    HoldPrevious,

    // Send nothing : the change is under the threshold and the output
    // keeps its last state on its own.
    Drop,
}

// ChangeThreshold, on every transport. A change is the sum of absolute
// channel deltas against the last pushed colour, on the 0-765 scale.
//
//   - The first push after start always goes out (no previous colour).
//   - A change to exact black always goes out, so a lamp can always turn
//     off whatever the threshold.
//   - A change at or above the threshold goes out.
//   - A smaller change is held on a continuous output (Hue Entertainment
//     streams a full frame on every tick, so the previous colour is sent
//     again) and dropped on a persistent one (Hue REST keeps its state).
internal static class AmbientPushGate
{
    public static AmbientPushDecision Decide(
        LightColor target,
        (int R, int G, int B) previous,
        int threshold,
        bool requiresContinuousColorUpdates)
    {
        if (previous.R < 0)
            return AmbientPushDecision.PushNew;

        bool previousIsBlack = previous is (0, 0, 0);
        if (target == LightColor.Black && !previousIsBlack)
            return AmbientPushDecision.PushNew;

        int delta = Math.Abs(target.R - previous.R)
                  + Math.Abs(target.G - previous.G)
                  + Math.Abs(target.B - previous.B);
        if (delta >= threshold)
            return AmbientPushDecision.PushNew;

        return requiresContinuousColorUpdates
            ? AmbientPushDecision.HoldPrevious
            : AmbientPushDecision.Drop;
    }

    // The colour a hold re-sends.
    public static LightColor PreviousColor((int R, int G, int B) previous)
        => new((byte)previous.R, (byte)previous.G, (byte)previous.B);
}
