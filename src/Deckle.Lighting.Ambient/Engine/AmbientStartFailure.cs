using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

/// <summary>Why the last start of Ambient Light failed, in the terms the
/// Settings page can say to the person. Ambient runs through a Hue
/// Entertainment area only, so the two causes that only the person can fix in
/// the Hue app or by pairing again are named; every other failure is
/// <see cref="Other"/>.</summary>
public enum AmbientStartFailure
{
    /// <summary>A failure with no advice of its own: the bridge did not
    /// answer, the display could not be captured, the stream did not open.</summary>
    Other,

    /// <summary>The bridge was paired without the Entertainment client key;
    /// pairing it again obtains one.</summary>
    NoClientKey,

    /// <summary>No Hue Entertainment area with lights backs the selected
    /// room; one is created in the Hue app.</summary>
    NoEntertainmentArea,
}

internal static class AmbientStartFailures
{
    /// <summary>The failure a start exception stands for.</summary>
    public static AmbientStartFailure From(Exception ex) => ex switch
    {
        HueEntertainmentUnavailableException { Reason: HueEntertainmentUnavailableReason.NoClientKey }
            => AmbientStartFailure.NoClientKey,
        HueEntertainmentUnavailableException { Reason: HueEntertainmentUnavailableReason.NoEntertainmentArea }
            => AmbientStartFailure.NoEntertainmentArea,
        _ => AmbientStartFailure.Other,
    };

    /// <summary>The reason the start-failed log line carries.</summary>
    public static string LogReason(Exception ex) => From(ex) switch
    {
        AmbientStartFailure.NoClientKey         => "no_client_key",
        AmbientStartFailure.NoEntertainmentArea => "no_entertainment_area",
        _                                       => "exception",
    };
}
