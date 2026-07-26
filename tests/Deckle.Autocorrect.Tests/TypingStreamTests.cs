using System.Collections.Generic;
using System.Linq;
using System.Text;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The typing-stream accumulator — the substrate of the mistouch-mining and
// natural-language corpora. These pin the reconstruction contract: runs chain
// into a span (append text, delete the next run's Erased, append its text),
// "repair" and "cap" continue the span while every other closure ends it, a
// dangling repair flushes as a text-less record, and the per-char timing string
// stays aligned to the text.
[Trait("Category", "unit")]
public class TypingStreamTests
{
    private static (TypingStream stream, List<TypingStream.RunRecord> done) New()
    {
        var done = new List<TypingStream.RunRecord>();
        var stream = new TypingStream { Completed = done.Add };
        return (stream, done);
    }

    private static void Type(TypingStream s, string text, double startMs = 0, double stepMs = 0)
    {
        double t = startMs;
        foreach (char c in text)
        {
            s.OnKeystroke(new Keystroke(KeystrokeKind.Text, c.ToString(), t));
            if (stepMs > 0) t += stepMs;
        }
    }

    private static void Backspace(TypingStream s, int count, double timestampMs = 0)
    {
        for (int i = 0; i < count; i++)
            s.OnKeystroke(Keystroke.Of(KeystrokeKind.Backspace, timestampMs));
    }

    // Replays a span the way a reader would: append each run's text after
    // deleting its Erased chars off what came before.
    private static string Replay(IEnumerable<TypingStream.RunRecord> runs)
    {
        var screen = new StringBuilder();
        foreach (var run in runs)
        {
            screen.Length -= run.Erased;
            screen.Append(run.Text);
        }
        return screen.ToString();
    }

    [Fact]
    public void CleanSentenceEmitsOneRunOnEnter()
    {
        var (s, done) = New();
        Type(s, "le chat dort.");
        s.OnKeystroke(Keystroke.Of(KeystrokeKind.Enter, 0));

        var run = Assert.Single(done);
        Assert.Equal("le chat dort.", run.Text);
        Assert.Equal(0, run.Erased);
        Assert.Equal("enter", run.Closure);
    }

    [Fact]
    public void BackwardRepairSegmentsTheFlowAndReplayRestoresTheScreen()
    {
        // « chien » — back over « en », retype « at. » : the faulty form, the
        // erase and the retype must all survive, and replay must restore what
        // the screen ended on.
        var (s, done) = New();
        Type(s, "chien");
        Backspace(s, 2);
        Type(s, "at.");
        s.OnKeystroke(Keystroke.Of(KeystrokeKind.Enter, 0));

        Assert.Equal(2, done.Count);
        Assert.Equal(new TypingStream.RunRecord("chien", 0, "repair", ""), done[0]);
        Assert.Equal(new TypingStream.RunRecord("at.", 2, "enter", ""), done[1]);
        Assert.Equal("chiat.", Replay(done));
    }

    [Fact]
    public void DanglingRepairFlushesAsATextlessRecord()
    {
        // Erase without retyping, then leave the field: the erase count must
        // not vanish — replay still knows those chars went away.
        var (s, done) = New();
        Type(s, "oups");
        Backspace(s, 3);
        s.NotifyFocusChanged();

        Assert.Equal(2, done.Count);
        Assert.Equal("repair", done[0].Closure);
        Assert.Equal(new TypingStream.RunRecord("", 3, "focus", ""), done[1]);
        Assert.Equal("o", Replay(done));
    }

    [Fact]
    public void SpanStartEraseIsRecordedAsUnknownTerritory()
    {
        // Backing into text typed before the span (or before the stream was
        // fed): the first run carries the erase count all the same.
        var (s, done) = New();
        Backspace(s, 2);
        Type(s, "isait beau.");
        s.OnKeystroke(Keystroke.Of(KeystrokeKind.Enter, 0));

        var run = Assert.Single(done);
        Assert.Equal(2, run.Erased);
        Assert.Equal("isait beau.", run.Text);
    }

    [Fact]
    public void EachResetKindClosesWithItsOwnName()
    {
        var (s, done) = New();
        Type(s, "a");
        s.OnKeystroke(Keystroke.Of(KeystrokeKind.Navigation, 0));
        Type(s, "b");
        s.NotifyPointerInteraction();
        Type(s, "c");
        s.OnKeystroke(Keystroke.Of(KeystrokeKind.Shortcut, 0));

        Assert.Equal(new[] { "navigation", "pointer", "shortcut" },
            done.Select(r => r.Closure).ToArray());
    }

    [Fact]
    public void ExternalMutationEndsTheReplayableSpan()
    {
        var (s, done) = New();
        Type(s, "avant");

        s.NotifyExternalMutation();
        Type(s, "après");
        s.NotifyFocusChanged();

        Assert.Equal(new[] { "external", "focus" },
            done.Select(r => r.Closure).ToArray());
    }

    [Fact]
    public void EmptyStateEmitsNothingOnReset()
    {
        var (s, done) = New();
        s.NotifyFocusChanged();
        s.OnKeystroke(Keystroke.Of(KeystrokeKind.Enter, 0));
        s.NotifyPointerInteraction();

        Assert.Empty(done);
    }

    [Fact]
    public void CapSplitsTheRunAndTheSpanContinues()
    {
        var (s, done) = New();
        Type(s, new string('x', TypingStream.RunCap + 3));
        s.OnKeystroke(Keystroke.Of(KeystrokeKind.Enter, 0));

        Assert.Equal(2, done.Count);
        Assert.Equal("cap", done[0].Closure);
        Assert.Equal(TypingStream.RunCap, done[0].Text.Length);
        Assert.Equal(0, done[1].Erased); // a storage split, not a repair
        Assert.Equal(new string('x', TypingStream.RunCap + 3), Replay(done));
    }

    [Fact]
    public void TimingCarriesPerCharGapsAndSurvivesTheRepairPause()
    {
        var (s, done) = New();
        Type(s, "ab", startMs: 1000, stepMs: 100);      // a@1000, b@1100
        Backspace(s, 1, timestampMs: 1200);
        Type(s, "c", startMs: 1500);
        s.OnKeystroke(Keystroke.Of(KeystrokeKind.Enter, 0));

        Assert.Equal("0,100", done[0].Timing);
        // The retype's first gap counts from the backspace — the repair pause
        // the pause-pass calibration will want.
        Assert.Equal("300", done[1].Timing);
    }

    [Fact]
    public void StamplessKeystrokesLeaveTimingEmpty()
    {
        var (s, done) = New();
        Type(s, "abc");
        s.OnKeystroke(Keystroke.Of(KeystrokeKind.Enter, 0));

        Assert.Equal(string.Empty, Assert.Single(done).Timing);
    }

    [Fact]
    public void DiscardDropsEverythingInFlightWithoutEmitting()
    {
        var (s, done) = New();
        Type(s, "secret en cours");
        Backspace(s, 2);
        s.Discard();
        s.NotifyFocusChanged();

        // The run already closed by the repair was emitted before the discard;
        // nothing accumulated after it may surface.
        Assert.Equal("repair", Assert.Single(done).Closure);
    }
}
