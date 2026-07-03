using System.Text;

namespace Deckle.Lighting;

// Builds HueStream v2 datagrams for a Hue Entertainment configuration.
// The transport is a separate concern: callers hand the returned bytes to
// the DTLS session. Keeping the frame format isolated makes it testable
// without a bridge and keeps the protocol detail inside the Hue driver.
internal static class HueEntertainmentFrameBuilder
{
    public const int MaxChannelsPerFrame = 20;

    private static readonly byte[] ProtocolName = Encoding.ASCII.GetBytes("HueStream");

    public static IReadOnlyList<byte[]> BuildFrames(
        string entertainmentConfigurationId,
        IReadOnlyList<HueEntertainmentChannelColor> colors,
        byte sequence = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entertainmentConfigurationId);
        ArgumentNullException.ThrowIfNull(colors);

        if (colors.Count == 0) return [];

        var frames = new List<byte[]>((colors.Count + MaxChannelsPerFrame - 1) / MaxChannelsPerFrame);
        byte[] configurationIdBytes = Encoding.ASCII.GetBytes(entertainmentConfigurationId.ToLowerInvariant());

        for (int offset = 0; offset < colors.Count; offset += MaxChannelsPerFrame)
        {
            int count = Math.Min(MaxChannelsPerFrame, colors.Count - offset);
            var frame = new byte[ProtocolName.Length + 7 + configurationIdBytes.Length + count * 7];
            int cursor = 0;

            ProtocolName.CopyTo(frame, cursor);
            cursor += ProtocolName.Length;

            frame[cursor++] = 0x02;      // version major
            frame[cursor++] = 0x00;      // version minor
            frame[cursor++] = unchecked((byte)(sequence + frames.Count)); // sequence number
            frame[cursor++] = 0x00;      // reserved
            frame[cursor++] = 0x00;      // reserved
            frame[cursor++] = 0x00;      // RGB colour mode
            frame[cursor++] = 0x00;      // no linear filter

            configurationIdBytes.CopyTo(frame, cursor);
            cursor += configurationIdBytes.Length;

            for (int i = 0; i < count; i++)
            {
                var c = colors[offset + i];
                if (c.ChannelId is < 0 or > 255)
                    throw new ArgumentOutOfRangeException(nameof(colors), "Hue Entertainment channel ids must fit in one byte.");

                frame[cursor++] = (byte)c.ChannelId;
                frame[cursor++] = c.Color.R;
                frame[cursor++] = c.Color.R;
                frame[cursor++] = c.Color.G;
                frame[cursor++] = c.Color.G;
                frame[cursor++] = c.Color.B;
                frame[cursor++] = c.Color.B;
            }

            frames.Add(frame);
        }

        return frames;
    }
}

internal readonly record struct HueEntertainmentChannelColor(int ChannelId, LightColor Color);
