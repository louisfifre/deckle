namespace Deckle.Input.PrecisionScroll;

public sealed class PrecisionScrollSettings
{
    public bool Enabled { get; set; } = false;

    public PrecisionScrollTuning Tuning { get; set; } = new();
}
