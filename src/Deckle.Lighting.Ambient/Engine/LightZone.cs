namespace Deckle.Lighting.Ambient;

// Which border of the screen a light reflects. Each light is assigned
// exactly one zone ; the engine computes one colour per zone (the mean of
// the zone's border band) and pushes it to every light assigned to it.
//
// A discrete edge rather than free (X, Y) coordinates, because most
// ambient setups put one lamp per edge, and dragging a marker to "above
// the screen" fights that mental model. It is HyperHDR's "N LEDs on one
// edge with a depth fraction" scheme collapsed to one light per edge.
public enum LightZone
{
    /// <summary>The light is not driven by the ambient pipeline.
    /// Selected lights stay at whatever colour the bridge last had ;
    /// the engine never touches them. Useful when the user has more
    /// Hue lights in the group than they want to colour-cycle.</summary>
    None,

    Top,

    Bottom,

    Left,

    Right,
}
