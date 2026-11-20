using Xunit;

namespace Deckle.Transcription.Parakeet.Decoding;

[Trait("Category", "unit")]
public sealed class TdtGreedyDecoderTests
{
    private const int Blank = 4;
    private const int Vocab = 5;

    /// <summary>Joint whose answer depends only on (frame index, call number on that frame); frames carry their index.</summary>
    private sealed class ScriptedJoint : ITdtJoint
    {
        private readonly Func<int, int, (int Token, int Duration)> _script;
        private readonly Dictionary<int, int> _callsPerFrame = [];
        public List<(int Frame, int LastToken, TdtDecoderState State)> Calls { get; } = [];
        public Action? OnStep { get; init; }

        public ScriptedJoint(Func<int, int, (int Token, int Duration)> script) => _script = script;

        public TdtDecoderState CreateInitialState() => new([0], [0]);

        public TdtJointOutput Step(ReadOnlyMemory<float> encoderFrame, int lastToken, TdtDecoderState state)
        {
            OnStep?.Invoke();
            int frame = (int)encoderFrame.Span[0];
            _callsPerFrame[frame] = _callsPerFrame.GetValueOrDefault(frame) + 1;
            Calls.Add((frame, lastToken, state));
            (int token, int duration) = _script(frame, _callsPerFrame[frame] - 1);

            var tokenLogits = new float[Vocab];
            tokenLogits[token] = 5f;
            var durationLogits = new float[5];
            durationLogits[duration] = 1f;
            // The new state is stamped with the call number so tests can tell which one was committed.
            return new TdtJointOutput(tokenLogits, durationLogits, new([Calls.Count], [Calls.Count]));
        }
    }

    private static IReadOnlyList<ReadOnlyMemory<float>> Frames(int count) =>
        Enumerable.Range(0, count).Select(i => (ReadOnlyMemory<float>)new float[] { i }).ToArray();

    private static TdtGreedyDecoder Decoder(ITdtJoint joint, int max = 10) => new(joint, Blank, max);

    [Fact]
    public void BlankRunEmitsNothingAndAdvancesOneFramePerStep()
    {
        var joint = new ScriptedJoint((_, _) => (Blank, 0));

        var tokens = Decoder(joint).Decode(Frames(4), TestContext.Current.CancellationToken);

        Assert.Empty(tokens);
        Assert.Equal([0, 1, 2, 3], joint.Calls.Select(c => c.Frame));
    }

    [Fact]
    public void NoFramesMeansNoStep()
    {
        var joint = new ScriptedJoint((_, _) => (0, 0));

        Assert.Empty(Decoder(joint).Decode(Frames(0), TestContext.Current.CancellationToken));
        Assert.Empty(joint.Calls);
    }

    [Fact]
    public void SeveralTokensOnOneFrameStayOnThatFrameUntilBlank()
    {
        // Frame 0: tokens 1 then 2 (duration 0), then blank moves on.
        var joint = new ScriptedJoint((f, n) => f == 0 ? (n switch { 0 => 1, 1 => 2, _ => Blank }, 0) : (Blank, 0));

        var tokens = Decoder(joint).Decode(Frames(2), TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], tokens.Select(t => t.Id));
        Assert.Equal([0, 0], tokens.Select(t => t.Frame));
        Assert.Equal([0, 0, 0, 1], joint.Calls.Select(c => c.Frame));
    }

    [Fact]
    public void StartTokenIsBlankThenTheLastEmittedToken()
    {
        var joint = new ScriptedJoint((f, n) => f == 0 && n < 2 ? (n == 0 ? 1 : 3, 0) : (Blank, 0));

        Decoder(joint).Decode(Frames(1), TestContext.Current.CancellationToken);

        Assert.Equal([Blank, 1, 3], joint.Calls.Select(c => c.LastToken));
    }

    [Fact]
    public void StateIsCommittedOnlyOnNonBlank()
    {
        // Call 1 emits a token (state stamp 1), call 2 is blank (stamp 2, discarded), call 3 must still receive stamp 1.
        var script = new Queue<int>([1, Blank, Blank]);
        var seq = new ScriptedJoint((_, _) => (script.Dequeue(), 0));

        Decoder(seq).Decode(Frames(2), TestContext.Current.CancellationToken);

        Assert.Equal(0f, seq.Calls[0].State.Hidden[0]);
        Assert.Equal(1f, seq.Calls[1].State.Hidden[0]);
        Assert.Equal(1f, seq.Calls[2].State.Hidden[0]);
    }

    [Fact]
    public void DurationJumpSkipsFramesAndRecordsTheEmissionFrame()
    {
        // Frame 0: token with duration 3 -> next call lands on frame 3. Frame 3: blank.
        var joint = new ScriptedJoint((f, _) => f == 0 ? (2, 3) : (Blank, 0));

        var tokens = Decoder(joint).Decode(Frames(6), TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(tokens).Id);
        Assert.Equal(0, tokens[0].Frame);
        Assert.Equal([0, 3, 4, 5], joint.Calls.Select(c => c.Frame));
    }

    [Fact]
    public void BlankWithPositiveDurationJumpsByThatDuration()
    {
        var joint = new ScriptedJoint((_, _) => (Blank, 2));

        Decoder(joint).Decode(Frames(5), TestContext.Current.CancellationToken);

        Assert.Equal([0, 2, 4], joint.Calls.Select(c => c.Frame));
    }

    [Fact]
    public void JumpPastTheEndStopsTheLoop()
    {
        var joint = new ScriptedJoint((_, _) => (0, 4));

        var tokens = Decoder(joint).Decode(Frames(3), TestContext.Current.CancellationToken);

        Assert.Single(tokens);
        Assert.Single(joint.Calls);
    }

    [Fact]
    public void MaxTokensPerStepForcesAdvanceToNextFrame()
    {
        // Always a token with duration 0: the cap (3) must move to the next frame.
        var joint = new ScriptedJoint((_, _) => (0, 0));

        var tokens = Decoder(joint, max: 3).Decode(Frames(2), TestContext.Current.CancellationToken);

        Assert.Equal([0, 0, 0, 1, 1, 1], tokens.Select(t => t.Frame));
        Assert.Equal(6, joint.Calls.Count);
    }

    [Fact]
    public void DefaultCapIsTen()
    {
        var joint = new ScriptedJoint((_, _) => (0, 0));

        var tokens = new TdtGreedyDecoder(joint, Blank).Decode(Frames(1), TestContext.Current.CancellationToken);

        Assert.Equal(10, tokens.Count);
    }

    [Fact]
    public void PositiveDurationResetsTheTokensPerStepCount()
    {
        // 2 tokens on frame 0 (cap 3), then a token with a jump of 1 (the third, not capped), then frame 1 gets a fresh count.
        var joint = new ScriptedJoint((f, n) => f == 0 ? (0, n == 2 ? 1 : 0) : (Blank, 0));

        var tokens = Decoder(joint, max: 3).Decode(Frames(2), TestContext.Current.CancellationToken);

        Assert.Equal([0, 0, 0], tokens.Select(t => t.Frame));
        Assert.Equal([0, 0, 0, 1], joint.Calls.Select(c => c.Frame));
    }

    [Fact]
    public void ProbabilityIsTheSoftmaxOfTheChosenToken()
    {
        var joint = new FixedLogitsJoint([2f, 0f, 0f, 0f, 1f]);

        var tokens = Decoder(joint).Decode(Frames(1), TestContext.Current.CancellationToken);

        double expected = Math.Exp(2) / (Math.Exp(2) + 3 + Math.Exp(1));
        Assert.Equal(expected, tokens[0].Probability, 5);
    }

    [Fact]
    public void ArgMaxTieTakesTheFirstIndex()
    {
        var joint = new FixedLogitsJoint([1f, 1f, 0f, 0f, 0f]);

        var tokens = Decoder(joint).Decode(Frames(1), TestContext.Current.CancellationToken);

        Assert.Equal(0, tokens[0].Id);
    }

    [Fact]
    public void CancellationIsCheckedBetweenSteps()
    {
        using var cts = new CancellationTokenSource();
        var joint = new ScriptedJoint((_, _) => (Blank, 0)) { OnStep = cts.Cancel };

        Assert.Throws<OperationCanceledException>(() => Decoder(joint).Decode(Frames(10), cts.Token));

        Assert.Single(joint.Calls);
    }

    [Fact]
    public void AlreadyCancelledTokenDoesNoStep()
    {
        var joint = new ScriptedJoint((_, _) => (Blank, 0));

        Assert.Throws<OperationCanceledException>(() => Decoder(joint).Decode(Frames(3), new CancellationToken(true)));

        Assert.Empty(joint.Calls);
    }

    /// <summary>Always answers the same token logits with a duration of 1 frame.</summary>
    private sealed class FixedLogitsJoint(float[] logits) : ITdtJoint
    {
        public TdtDecoderState CreateInitialState() => new([0], [0]);

        public TdtJointOutput Step(ReadOnlyMemory<float> encoderFrame, int lastToken, TdtDecoderState state) =>
            new(logits, [0f, 1f, 0f, 0f, 0f], state);
    }
}
