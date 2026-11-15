namespace Deckle.Projects;

// What the button of an action does right now: start it, or bring its terminal
// to the front once it runs (spec, Pinned actions: "Run" gives way to "Show
// terminal").
public enum ActionButtonMode
{
    Run,
    ShowTerminal,
}

// How a pinned action's card reads: its state line, the button it offers and
// whether that button can be pressed.
public sealed record PinnedCardState(RunLine Line, ActionButtonMode Button, bool ButtonEnabled);

// How the project page's header reads for the main action: the accent button
// (named after the action), and the one line under the header's buttons, which
// is null while the main action has not run.
public sealed record HeaderActionState(string Label, ActionButtonMode Button, bool ButtonEnabled, RunLine? Line);

// The rules that turn what the board knows into what a card and the header
// say, in one place so a test can hold them to the spec's sentences and the
// two surfaces cannot disagree (spec, Pinned actions and P3).
//
//   an action of the project runs   the card of that action shows its running
//                                   line and "Show terminal"; every other card
//                                   waits for it and cannot run; the header's
//                                   accent button is disabled
//   nothing runs                    each card shows its last result, and "Run"
//                                   is available unless the menu no longer gives
//                                   the action or the project cannot start one
//
// canStart is the engine's own list of reasons to refuse (the folder is there,
// the menu is there, a shell is there), which the page already knows; the
// runner stays the safety net behind a button that is wrongly enabled.
public static class ActionStates
{
    public static PinnedCardState ForCard(
        PinnedAction pin, ActionRun? running, ActionResult? last, bool menuGivesIt, bool canStart, DateTimeOffset now)
    {
        if (running is not null)
        {
            return running.Action == pin.Action
                ? new PinnedCardState(RunStateLine.Running(running, now), ActionButtonMode.ShowTerminal, ButtonEnabled: true)
                : new PinnedCardState(RunStateLine.Waiting(running.Label), ActionButtonMode.Run, ButtonEnabled: false);
        }

        if (!menuGivesIt)
            return new PinnedCardState(RunStateLine.Gone(), ActionButtonMode.Run, ButtonEnabled: false);

        return new PinnedCardState(RunStateLine.Idle(last, now), ActionButtonMode.Run, canStart);
    }

    public static HeaderActionState ForHeader(
        PinnedAction main, ActionRun? running, ActionResult? last, bool canStart, DateTimeOffset now)
    {
        if (running is not null && running.Action == main.Action)
        {
            // The main action runs: the line says so and the accent button
            // gives way to "Show terminal".
            return new HeaderActionState(
                main.Label, ActionButtonMode.ShowTerminal, ButtonEnabled: true, RunStateLine.Main(main.Label, last, running, now));
        }

        // Another action runs: the main action's own last result stays on its
        // line (P3: the failure of "Build & run" stays while "Self-check" runs)
        // and the accent button waits.
        RunLine? line = RunStateLine.Main(main.Label, last, running: null, now);
        return new HeaderActionState(main.Label, ActionButtonMode.Run, running is null && canStart, line);
    }
}
