namespace Deckle.Llm.Rewrite;

public enum ParagraphRewriteHostAction { None, Start, Stop }

// The one decision of the host lifecycle: the global keyboard and mouse host
// is held while paragraph rewriting is switched on AND the PC can serve it,
// and not otherwise. Pure, so the App's reconcile carries no logic of its own.
public static class ParagraphRewriteHostPlan
{
    public static bool ShouldRun(bool enabled, ParagraphRewriteAvailability availability) =>
        enabled && availability == ParagraphRewriteAvailability.Available;

    public static ParagraphRewriteHostAction Next(
        bool running, bool enabled, ParagraphRewriteAvailability availability)
    {
        bool shouldRun = ShouldRun(enabled, availability);
        if (shouldRun == running) return ParagraphRewriteHostAction.None;
        return shouldRun ? ParagraphRewriteHostAction.Start : ParagraphRewriteHostAction.Stop;
    }
}
