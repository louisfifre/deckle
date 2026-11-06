using Deckle.Audio;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "regression")]
public sealed class SegmentedTranscriptionSessionTests
{
    private const int FrameSamples = 800;
    private static readonly double VoiceLevel = new CaptureSettings().VoiceLevelDbfs;

    // Five voiced frames: exactly the 250 ms floor, so each burst is kept.
    private const int MinSpeechMs = 250;

    [Fact]
    public async Task AnyCaptureFrameProducerReachesTheSameOrderedConsumer()
    {
        var releaseConsumer = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var consumed = new List<int>();
        var session = new SegmentedTranscriptionSession<int>(
            Settings(),
            VoiceLevel,
            MinSpeechMs,
            async (reader, onDequeue) =>
            {
                await releaseConsumer.Task;
                await foreach (Utterance utterance in reader.ReadAllAsync())
                {
                    onDequeue();
                    consumed.Add(utterance.Index);
                }
                return consumed.Count;
            });

        EmitUtterance(session);
        EmitUtterance(session);

        Assert.Equal(2, session.Backlog);

        session.Complete();
        releaseConsumer.SetResult();
        int count = await session.Completion;

        Assert.Equal(2, count);
        Assert.Equal([0, 1], consumed);
        Assert.Equal(0, session.Backlog);
    }

    [Fact]
    public async Task CompleteFlushesTheOpenUtteranceBeforeClosingTheConsumer()
    {
        var consumed = new List<Utterance>();
        var session = new SegmentedTranscriptionSession<int>(
            Settings(),
            VoiceLevel,
            MinSpeechMs,
            async (reader, onDequeue) =>
            {
                await foreach (Utterance utterance in reader.ReadAllAsync())
                {
                    onDequeue();
                    consumed.Add(utterance);
                }
                return consumed.Count;
            });

        for (int i = 0; i < 5; i++)
            session.Push(Frame(voiced: true));

        session.Complete();

        Assert.Equal(1, await session.Completion);
        Assert.Single(consumed);
    }

    private static void EmitUtterance(
        SegmentedTranscriptionSession<int> session)
    {
        for (int i = 0; i < 5; i++)
            session.Push(Frame(voiced: true));
        session.Push(Frame(voiced: false));
    }

    private static CaptureFrame Frame(bool voiced) =>
        new(new float[FrameSamples], voiced ? 0.1f : 0f);

    private static EnergySegmenterSettings Settings() => new()
    {
        HangoverMaxMs = 50,
        HangoverMinMs = 50,
        HangoverRampStartMs = 50,
        HangoverRampEndMs = 50,
        MarginMs = 0,
    };
}
