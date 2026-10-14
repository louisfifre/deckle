namespace Deckle.Composition;

// What the cone is painted with: a flat grey (one OKLab lightness per theme)
// or the OKLCh colour wheel. Each HUD state picks one in
// ConicArcStrokeConfig, and MaterialOf turns that choice into the mix of the
// CrossFadeEffect that joins the two. The values are pinned because the HUD
// settings file stores this enum as a number: a reordered member must never
// flip a stored choice.
public enum HudMaterial
{
    Grey = 0,
    Colour = 1,
}
