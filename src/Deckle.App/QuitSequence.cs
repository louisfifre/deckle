using Deckle.Transcription;

namespace Deckle.App;

// The order of a quit that found work in flight: finish the work, then exit,
// with nothing between the two. Whatever the drain returns, finished or timed
// out, the exit follows at once; a drain that throws is reported and the exit
// still follows. Kept apart from App.QuitApp so a test can perform the order
// without the WinUI host.
internal static class QuitSequence
{
    public static void Run(Func<DrainResult> finishInFlightWork, Action<Exception> drainFailed, Action exit)
    {
        try { finishInFlightWork(); }
        catch (Exception ex) { drainFailed(ex); }

        exit();
    }
}
