using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// What the timeline of the dictation steps draws (Page 7 of
// specs/deckle-dictation-rewriting.md), with the example settings of the
// spec: Affinage first with "Start at" 1,000, Lissage from 4,000, Arrangement
// from 12,000 at Stop.
[Trait("Category", "unit")]
public sealed class StepTimelineLayoutTests
{
    private static DictationRewriteSettings Example(bool startAt = true) => new()
    {
        Enabled = true,
        StartAtLength = startAt,
        RewriteWhileSpeaking = true,
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

    [Fact]
    public void TheExampleReadsAsTheMockUpShowsIt()
    {
        StepTimelineLayout layout = StepTimelineLayout.For(Example(), Name, transcriptionStreams: true);

        Assert.Equal([0, 1000, 4000, 12000], layout.Lengths);
        Assert.Equal(
            [
                new TimelineSegment(0, 1, null, AsSaid: true),
                new TimelineSegment(1, 2, "Affinage", AsSaid: false),
                new TimelineSegment(2, 3, "Lissage", AsSaid: false),
                new TimelineSegment(3, 4, "Arrangement", AsSaid: false),
            ],
            layout.Streaming);
        Assert.Empty(layout.AtStop);
    }

    [Fact]
    public void WithoutStartAtTheFirstStepRunsFromTheBeginning()
    {
        StepTimelineLayout layout = StepTimelineLayout.For(Example(startAt: false), Name, transcriptionStreams: true);

        Assert.Equal([0, 4000, 12000], layout.Lengths);
        Assert.Equal(new TimelineSegment(0, 1, "Affinage", AsSaid: false), layout.Streaming[0]);
    }

    // Page 8: without streaming transcription, every step lands at Stop.
    [Fact]
    public void WithoutStreamingTranscriptionEveryStepIsAtStop()
    {
        StepTimelineLayout layout = StepTimelineLayout.For(Example(), Name, transcriptionStreams: false);

        Assert.Equal([new TimelineSegment(0, 4, null, AsSaid: true)], layout.Streaming);
        Assert.Equal(["Affinage", "Lissage", "Arrangement"], layout.AtStop.Select(segment => segment.Name));
    }

    [Fact]
    public void AStepWhoseProfileIsGoneShowsItsId()
    {
        DictationRewriteSettings dictation = Example();
        dictation.Steps[2].ProfileId = "gone";

        StepTimelineLayout layout = StepTimelineLayout.For(dictation, Name, transcriptionStreams: true);

        Assert.Equal("gone", layout.Streaming[^1].Name);
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
