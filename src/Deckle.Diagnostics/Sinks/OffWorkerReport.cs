namespace Deckle.Diagnostics;

// Where a sink's own report leaves its worker.
//
// Emitting on the worker would dispatch back into the sink's bounded queue;
// with the queue full the worker would wait on itself. A pool thread breaks
// that cycle. The catch is there because nothing awaits the task: an exception
// left in it would surface later as an unobserved task fault and be reported as
// a crash, for a line that only failed to be written.
internal static class OffWorkerReport
{
    // onWorker is false while a constructor runs: no worker exists yet, so the
    // report can be written in place.
    public static void Run(bool onWorker, Action report)
    {
        if (!onWorker)
        {
            report();
            return;
        }

        _ = Task.Run(() =>
        {
            try { report(); }
            catch { }
        });
    }
}
