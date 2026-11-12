using System.Linq;

namespace Deckle.Llm.Rewrite;

// What the timeline of the dictation steps draws (specs/deckle-dictation-
// rewriting.md, Page 9 to 12), apart from how it is drawn so a test can read
// it.
//
// One row per step, in step order, the same rows in both modes; each row
// holds spans on one axis of characters, proportional, from 0 to AxisChars:
//   - "Rewrite while I speak" off (or no streaming transcription): one span,
//     the range of lengths the step takes, from its length to the next
//     step's (the last one to the end of the axis);
//   - on: the step's blocks, from the beginning of the dictation to where the
//     next step takes over. The blocks that end before the step's own length
//     are Pale: the text it rewrites at once when it takes over.
//
// The axis runs half as far again past the last step's length, so the last
// step shows a stretch of its own, and ends on a tick. The ticks fall on the
// 1-2-5 ladder, four intervals or fewer, so their labels fit a settings
// card's width.
public sealed record StepTimelineLayout(
    int AxisChars,
    IReadOnlyList<int> Ticks,
    IReadOnlyList<TimelineRow> Rows,
    bool WhileSpeaking)
{
    // An axis never shorter than this: a single step from the beginning
    // still shows about three minutes of speech.
    private const int MinAxisChars = 2000;
    private const int MaxIntervals = 4;

    // transcriptionStreams false: nothing is transcribed before Stop, so the
    // timeline shows every step's range whatever "Rewrite while I speak" says.
    // profileName names a step's profile; a profile that no longer exists
    // shows as its id, so the step is still told apart.
    public static StepTimelineLayout For(
        DictationRewriteSettings dictation,
        Func<string, string?> profileName,
        bool transcriptionStreams)
    {
        bool whileSpeaking = transcriptionStreams && dictation.RewriteWhileSpeaking;
        List<RewriteStep> steps = dictation.Steps;
        int lastStart = steps.Count > 0 ? dictation.StartOf(steps.Count - 1) : 0;

        int raw = Math.Max(lastStart + lastStart / 2, MinAxisChars);
        int interval = Interval(raw);
        int axis = (raw + interval - 1) / interval * interval;
        List<int> ticks = Enumerable.Range(0, axis / interval + 1).Select(i => i * interval).ToList();

        var rows = new List<TimelineRow>(steps.Count);
        for (int i = 0; i < steps.Count; i++)
        {
            RewriteStep step = steps[i];
            int from = dictation.StartOf(i);
            int to = i + 1 < steps.Count ? dictation.StartOf(i + 1) : axis;
            int block = Math.Clamp(step.BlockChars,
                DictationRewriteSettings.MinBlockChars, DictationRewriteSettings.MaxBlockChars);

            var spans = new List<TimelineSpan>();
            if (whileSpeaking)
            {
                for (int start = 0; start < to; start += block)
                {
                    int end = Math.Min(start + block, to);
                    spans.Add(new TimelineSpan(start, end, Pale: end <= from));
                }
            }
            else
            {
                spans.Add(new TimelineSpan(from, to, Pale: false));
            }

            rows.Add(new TimelineRow(profileName(step.ProfileId) ?? step.ProfileId, from, to, block, spans));
        }

        return new StepTimelineLayout(axis, ticks, rows, whileSpeaking);
    }

    // The smallest step on the 1-2-5 ladder that cuts length into at most
    // MaxIntervals intervals.
    private static int Interval(int length)
    {
        for (int decade = 100; ; decade *= 10)
            foreach (int factor in (ReadOnlySpan<int>)[1, 2, 5])
                if (decade * factor * MaxIntervals >= length) return decade * factor;
    }
}

// One step's row: its name, the lengths it takes (FromChars to ToChars), its
// block size, and the spans drawn on it.
public sealed record TimelineRow(
    string Name, int FromChars, int ToChars, int BlockChars, IReadOnlyList<TimelineSpan> Spans);

// A stretch of the axis, FromChars to ToChars. Pale: a block the step
// rewrites at once when it takes over, before its own length.
public sealed record TimelineSpan(int FromChars, int ToChars, bool Pale);
