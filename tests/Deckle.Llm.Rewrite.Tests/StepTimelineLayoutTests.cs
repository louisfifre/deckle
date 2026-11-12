using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// What the timeline of the dictation steps draws (Page 9 to 12 of
// specs/deckle-dictation-rewriting.md), with the example settings of the
// spec: Affinage first with "Start at" 1,000 in blocks of 600, Lissage from
// 4,000 in blocks of 2,000, Arrangement from 12,000 in blocks of 6,000.
[Trait("Category", "unit")]
public sealed class StepTimelineLayoutTests
{
    private static DictationRewriteSettings Example(bool whileSpeaking, bool startAt = true) => new()
    {
        Enabled = true,
        StartAtLength = startAt,
        RewriteWhileSpeaking = whileSpeaking,
        Steps =
        [
            new RewriteStep { ProfileId = "aff", FromChars = 1000, BlockChars = 600 },
            new RewriteStep { ProfileId = "lis", FromChars = 4000, BlockChars = 2000 },
            new RewriteStep { ProfileId = "arr", FromChars = 12000, BlockChars = 6000 },
        ],
    };

    private static string? Name(string id) => id switch
    {
        "aff" => "Affinage",
        "lis" => "Lissage",
        "arr" => "Arrangement",
        _ => null,
    };

    // Page 9: the same rows in both modes, the name in each, and a scale
    // that runs past the last step on round lengths.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothModesHaveOneRowPerStepAndTheSameScale(bool whileSpeaking)
    {
        StepTimelineLayout layout = StepTimelineLayout.For(Example(whileSpeaking), Name, transcriptionStreams: true);

        Assert.Equal(["Affinage", "Lissage", "Arrangement"], layout.Rows.Select(row => row.Name));
        Assert.Equal(20000, layout.AxisChars);
        Assert.Equal([0, 5000, 10000, 15000, 20000], layout.Ticks);
        Assert.Equal(whileSpeaking, layout.WhileSpeaking);
    }

    // Page 10: off, each row is the range of lengths its step takes.
    [Fact]
    public void OffEachRowShowsTheRangeItsStepTakes()
    {
        StepTimelineLayout layout = StepTimelineLayout.For(Example(false), Name, transcriptionStreams: true);

        Assert.Equal(
            [[new TimelineSpan(1000, 4000, false)], [new TimelineSpan(4000, 12000, false)], [new TimelineSpan(12000, 20000, false)]],
            layout.Rows.Select(row => row.Spans.ToArray()));
    }

    // Page 11: on, each row is its step's blocks from the beginning to where
    // the next step takes over; those before the step's length are pale.
    [Fact]
    public void OnEachRowShowsItsBlocksAndTheOnesBeforeItsLengthArePale()
    {
        StepTimelineLayout layout = StepTimelineLayout.For(Example(true), Name, transcriptionStreams: true);

        Assert.Equal(
            [
                new TimelineSpan(0, 2000, true), new TimelineSpan(2000, 4000, true),
                new TimelineSpan(4000, 6000, false), new TimelineSpan(6000, 8000, false),
                new TimelineSpan(8000, 10000, false), new TimelineSpan(10000, 12000, false),
            ],
            layout.Rows[1].Spans);
        Assert.Equal(
            [new TimelineSpan(0, 6000, true), new TimelineSpan(6000, 12000, true),
             new TimelineSpan(12000, 18000, false), new TimelineSpan(18000, 20000, false)],
            layout.Rows[2].Spans);
        Assert.Equal(new TimelineSpan(0, 600, true), layout.Rows[0].Spans[0]);
        Assert.Equal(new TimelineSpan(3600, 4000, false), layout.Rows[0].Spans[^1]);
    }

    // Page 7: switching the mode moves no length.
    [Fact]
    public void SwitchingTheModeMovesNoLength()
    {
        var off = StepTimelineLayout.For(Example(false), Name, transcriptionStreams: true);
        var on = StepTimelineLayout.For(Example(true), Name, transcriptionStreams: true);

        Assert.Equal(off.Rows.Select(row => (row.FromChars, row.ToChars)), on.Rows.Select(row => (row.FromChars, row.ToChars)));
    }

    [Fact]
    public void WithoutStartAtTheFirstStepRunsFromTheBeginning()
    {
        StepTimelineLayout layout = StepTimelineLayout.For(Example(true, startAt: false), Name, transcriptionStreams: true);

        Assert.Equal(0, layout.Rows[0].FromChars);
        Assert.DoesNotContain(layout.Rows[0].Spans, span => span.Pale);
    }

    // Page 3: without streaming transcription, every step rewrites at Stop.
    [Fact]
    public void WithoutStreamingTranscriptionEveryRowShowsItsRange()
    {
        StepTimelineLayout layout = StepTimelineLayout.For(Example(true), Name, transcriptionStreams: false);

        Assert.False(layout.WhileSpeaking);
        Assert.All(layout.Rows, row => Assert.Single(row.Spans));
    }

    [Fact]
    public void AStepWhoseProfileIsGoneShowsItsId()
    {
        DictationRewriteSettings dictation = Example(false);
        dictation.Steps[2].ProfileId = "gone";

        StepTimelineLayout layout = StepTimelineLayout.For(dictation, Name, transcriptionStreams: true);

        Assert.Equal("gone", layout.Rows[2].Name);
    }
}
[Trait("Category", "unit")]
public sealed class RemoveProfileFromStepsTests
{
    [Fact]
    public void ALaterStepGoesWithItsProfileAndTheFirstStepTakesTheFirstProfileLeft()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);
        string first = settings.Profiles[0].Id, second = settings.Profiles[1].Id;
        settings.Dictation.Steps =
        [
            new RewriteStep { ProfileId = first },
            new RewriteStep { ProfileId = first, FromChars = 4000 },
            new RewriteStep { ProfileId = second, FromChars = 8000 },
        ];

        LlmSettingsNormalizer.RemoveProfileFromSteps(settings, first);

        Assert.Equal([second, second], settings.Dictation.Steps.Select(step => step.ProfileId));
        Assert.Equal([0, 8000], settings.Dictation.Steps.Select(step => step.FromChars));
    }
}
