using System.Linq;

namespace Deckle.Llm.Rewrite;

// What the timeline of the dictation steps draws (specs/deckle-dictation-
// rewriting.md, Page 7), apart from how it is drawn so a test can read it.
//
// The axis has one column per length where something starts, every column
// the same width whatever the characters between two lengths: a step at
// 30,000 characters (forty-five minutes) and one at 1,000 both stay readable,
// where a proportional axis would crush the short ones. The last column runs
// on past the highest length.
//
// Two lanes. Streaming: from each length, the profile of the highest
// streaming step reached, or "as said" before any; a step "at Stop" does not
// interrupt it (Panel 4). At stop: the profile of a step "at Stop" while it
// is the highest step reached, nothing elsewhere. Neighbouring columns with
// the same content make one segment.
public sealed record StepTimelineLayout(
    IReadOnlyList<int> Lengths,
    IReadOnlyList<TimelineSegment> Streaming,
    IReadOnlyList<TimelineSegment> AtStop)
{
    // transcriptionStreams false: no passage exists before Stop, so every
    // step lands on the "At stop" lane and the Streaming lane reads as said.
    // profileName names a step's profile; a profile that no longer exists
    // shows as its id, so the step is still told apart.
    public static StepTimelineLayout For(
        DictationRewriteSettings dictation,
        Func<string, string?> profileName,
        bool transcriptionStreams)
    {
        var steps = dictation.Steps
            .Select((step, i) => (
                From: dictation.StartOf(i),
                Name: profileName(step.ProfileId) ?? step.ProfileId,
                Streaming: transcriptionStreams && dictation.RewriteWhileSpeaking))
            .ToList();

        List<int> lengths = steps.Select(step => step.From).Prepend(0).Distinct().Order().ToList();

        var streaming = new List<TimelineSegment>();
        var atStop = new List<TimelineSegment>();
        for (int column = 0; column < lengths.Count; column++)
        {
            int at = lengths[column];
            var reached = steps.Where(step => step.From <= at).ToList();

            string? streamingName = reached.LastOrDefault(step => step.Streaming).Name;
            Extend(streaming, column, streamingName, asSaid: streamingName is null);

            if (reached.Count > 0 && !reached[^1].Streaming)
                Extend(atStop, column, reached[^1].Name, asSaid: false);
        }

        return new StepTimelineLayout(lengths, streaming, atStop);
    }

    // Lengthens the lane's last segment when it holds the same thing and ends
    // where this column starts, or opens a new one.
    private static void Extend(List<TimelineSegment> lane, int column, string? name, bool asSaid)
    {
        if (lane.Count > 0 && lane[^1] is var last
            && last.ToColumn == column && last.Name == name && last.AsSaid == asSaid)
        {
            lane[^1] = last with { ToColumn = column + 1 };
            return;
        }
        lane.Add(new TimelineSegment(column, column + 1, name, asSaid));
    }
}

// Columns FromColumn to ToColumn, exclusive. AsSaid: no profile rewrites the
// dictation there (Name is null).
public sealed record TimelineSegment(int FromColumn, int ToColumn, string? Name, bool AsSaid);
