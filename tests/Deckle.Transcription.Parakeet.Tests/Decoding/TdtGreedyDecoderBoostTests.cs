using Xunit;

namespace Deckle.Transcription.Parakeet.Decoding;

[Trait("Category", "unit")]
public sealed class TdtGreedyDecoderBoostTests
{
    // Ids 0..3 are text, 4 is the blank.
    private static readonly string[] Texts = [" la", " de", "ck", "le", ""];
    private const int La = 0, De = 1, Ck = 2, Le = 3, Blank = 4;

    /// <summary>Plays one logits vector per call, each with a one-frame jump, then blanks.</summary>
    private sealed class ScriptedLogitsJoint(params float[][] steps) : ITdtJoint
    {
        private int _call;

        public TdtDecoderState CreateInitialState() => new([0], [0]);

        public TdtJointOutput Step(ReadOnlyMemory<float> encoderFrame, int lastToken, TdtDecoderState state)
        {
            float[] logits = _call < steps.Length ? steps[_call] : [0, 0, 0, 0, 9];
            _call++;
            return new TdtJointOutput(logits, [0f, 1f], state);
        }
    }

    private static IReadOnlyList<ReadOnlyMemory<float>> Frames(int count) =>
        Enumerable.Range(0, count).Select(i => (ReadOnlyMemory<float>)new float[] { i }).ToArray();

    private static LexiconBoostTree Deckle(float strength = 2f) => LexiconBoostTree.Build(["Deckle"], Texts, strength)!;

    [Fact]
    public void ACloseCallTipsTowardTheTerm()
    {
        // The model hesitates between " la ck le" and " de ck le" and slightly prefers " la".
        var joint = new ScriptedLogitsJoint(
            [3.0f, 2.8f, 0, 0, 0],
            [0, 0, 3f, 0, 0],
            [0, 0, 0, 3f, 0]);
        var decoder = new TdtGreedyDecoder(joint, Blank, boost: Deckle());

        var tokens = decoder.Decode(Frames(3), TestContext.Current.CancellationToken);

        Assert.Equal([De, Ck, Le], tokens.Select(t => t.Id));
        Assert.Equal(1, decoder.BoostedTokens);
    }

    [Fact]
    public void AClearChoiceOfTheModelIsKept()
    {
        var joint = new ScriptedLogitsJoint([6f, 1f, 0, 0, 0]);
        var decoder = new TdtGreedyDecoder(joint, Blank, boost: Deckle());

        var tokens = decoder.Decode(Frames(1), TestContext.Current.CancellationToken);

        Assert.Equal(La, Assert.Single(tokens).Id);
        Assert.Equal(0, decoder.BoostedTokens);
    }

    [Fact]
    public void TheBoostNeverSpeaksOverABlank()
    {
        // The blank wins by a hair: even a strong boost must not insert the term into a silence.
        var joint = new ScriptedLogitsJoint([0, 2.9f, 0, 0, 3f]);
        var decoder = new TdtGreedyDecoder(joint, Blank, boost: Deckle(strength: 5f));

        Assert.Empty(decoder.Decode(Frames(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheConfidenceStaysTheModelsOwn()
    {
        float[] logits = [3.0f, 2.8f, 0, 0, 0];
        var decoder = new TdtGreedyDecoder(new ScriptedLogitsJoint(logits), Blank, boost: Deckle());

        var token = decoder.Decode(Frames(1), TestContext.Current.CancellationToken)[0];

        double expected = Math.Exp(2.8) / (Math.Exp(3.0) + Math.Exp(2.8) + 3);
        Assert.Equal(De, token.Id);
        Assert.Equal(expected, token.Probability, 5);
    }

    [Fact]
    public void WithoutBoostTheDecoderFollowsTheModel()
    {
        var joint = new ScriptedLogitsJoint([3.0f, 2.8f, 0, 0, 0]);

        var tokens = new TdtGreedyDecoder(joint, Blank).Decode(Frames(1), TestContext.Current.CancellationToken);

        Assert.Equal(La, Assert.Single(tokens).Id);
    }
}
