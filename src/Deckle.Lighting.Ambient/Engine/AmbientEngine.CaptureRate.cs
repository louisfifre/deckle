using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    /// <summary>
    /// Decides the capture rate for a run or a preview scan and logs the
    /// choice once: the display's announced refresh rate, held to the tier
    /// ceiling the performance mode selects (<see cref="AmbientCaptureCap"/>).
    /// Read at capture start only; a change of display or mode restarts the
    /// capture through the app, which builds a new service.
    /// </summary>
    private int ChooseCaptureRateHz(string workId, AmbientSettings ambient)
    {
        int displayHz = ScreenCaptureService.GetDisplayRefreshRateHz(ambient.SelectedMonitorDeviceName);
        bool performanceMode = _host.PerformanceMode;
        int capHz = AmbientCaptureCap.CapHz(performanceMode);
        int targetHz = AmbientCaptureCap.TargetHz(displayHz, performanceMode);

        DeckleAmbientSource.Log.CaptureRateChosen(
            workId, displayHz, capHz, performanceMode ? "on" : "off", targetHz);
        return targetHz;
    }
}
