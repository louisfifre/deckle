using Microsoft.UI.Dispatching;

namespace Deckle.Projects.Views;

// "Running · 1 min 12 s" and "Failed 4 min ago" move with the clock, so the
// states that show a time are drawn again once a second. The board raises its
// event when an action begins or ends and never for the time that passes; this
// tick is the only thing that makes the seconds count. Only the action states
// are redrawn, and a card whose words did not change is left alone.
internal sealed partial class ProjectPage
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private DispatcherQueueTimer? _tick;

    private void StartTimer()
    {
        _tick = DispatcherQueue.CreateTimer();
        _tick.Interval = Tick;
        _tick.IsRepeating = true;
        _tick.Tick += (_, _) => ApplyActions();
        _tick.Start();
    }

    private void StopTimer()
    {
        _tick?.Stop();
        _tick = null;
    }
}
