namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    /// <summary>
    /// The capture rate for a run or a preview scan, fixed at
    /// <see cref="AmbientCaptureCap.CaptureHz"/>, logged once so a person
    /// reading the log learns what the rate was.
    /// </summary>
    private static int ChooseCaptureRateHz(string workId)
    {
        DeckleAmbientSource.Log.CaptureRateChosen(workId, AmbientCaptureCap.CaptureHz);
        return AmbientCaptureCap.CaptureHz;
    }
}
