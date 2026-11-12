using System.Collections.Concurrent;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;
using Xunit;
using static Deckle.Transcription.Tests.LiveTranscriptionPipelineTests;
using Deckle.Localization;

namespace Deckle.Transcription.Tests;

// A live take against the dictation steps (specs/deckle-dictation-rewriting.md,
// groups Panel and Stop). The engine is driven through the seams it already
// has: a scripted microphone and a scripted backend stand in for the model, so
// each scripted utterance reaches the engine's UtteranceTranscribed path
// exactly as a spoken one does, and a rewrite service that tags each answer
// with its profile shows which step rewrote what.
[Trait("Category", "integration")]
public sealed class StreamingStepsRewriteTests
{
    // Each utterance is longer than the minimum block size (200) and ends on a
    // silence, so at that size each one closes a block of its own.
    private static readonly string First = Repeat("Je commence par le premier sujet de cette dictée. ");
    private static readonly string Second = Repeat("Puis je passe au second sujet, qui est différent. ");
    private static readonly string Third = Repeat("Enfin je termine par un dernier sujet, assez court. ");

    private static string Repeat(string sentence) =>
        string.Concat(Enumerable.Repeat(sentence, 5)).Trim();

    private static readonly string RawTake = First + "\n\n" + Second + "\n\n" + Third;

    // The raw length once the second utterance is in.
    private static readonly int AfterSecond = First.Length + 2 + Second.Length;

    private const int Min = DictationRewriteSettings.MinBlockChars;

    // Panel 2: the first step starts at a length the second utterance
    // reaches; the block closed before it leaves then, while the speaker is
    // still recording.
    [Fact]
    public async Task ReachingTheFirstStepSendsTheBlocksClosedBeforeIt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        StreamingHost host = StepsHost(whileSpeaking: true, ("tier1", First.Length + 1, Min));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        // Without rewriting while speaking nothing is sent before Stop and
        // this wait times out.
        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 2, ct);

        Assert.Equal(
            new[] { First, Second, Third }.Order(),
            rewrite.Calls.Select(c => c.Input).Order());
        Assert.All(rewrite.Calls, call => Assert.Equal("tier1", call.ProfileId));
        Assert.Equal("tier1[" + First + "]\n\ntier1[" + Second + "]\n\ntier1[" + Third + "]", clipboard.Text);
        Assert.InRange(clipboard.WriteCount, 1, 2);
    }

    // Behaviours 4: a higher step takes over, rewrites the beginning again
    // from the raw text with its own profile, then goes on alone.
    [Fact]
    public async Task AHigherStepRewritesTheBeginningAgainFromTheRawText()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        StreamingHost host = StepsHost(whileSpeaking: true, ("tier1", 0, Min), ("tier2", AfterSecond, Min));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        // tier1 on the first block, then tier2 on all three.
        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 4, ct);

        Assert.Equal([First], rewrite.Calls.Where(c => c.ProfileId == "tier1").Select(c => c.Input));
        Assert.Equal(
            new[] { First, Second, Third }.Order(),
            rewrite.Calls.Where(c => c.ProfileId == "tier2").Select(c => c.Input).Order());
        Assert.Equal("tier2[" + First + "]\n\ntier2[" + Second + "]\n\ntier2[" + Third + "]", clipboard.Text);
    }

    // Behaviours 4 and 5, Stop 2: a step with larger blocks takes over and
    // cuts what was already said into its own blocks (here the first two
    // blocks of the step before, as one); the block it had not finished is
    // rewritten at Stop.
    [Fact]
    public async Task AStepThatTakesOverCutsWhatWasSaidIntoItsOwnBlocks()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        int twoBlocks = First.Length + 2 + Second.Length;
        StreamingHost host = StepsHost(whileSpeaking: true, ("tier1", 0, Min), ("tier2", RawTake.Length, twoBlocks));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        // tier1 on the first two blocks, then tier2 on both as one.
        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 3, ct);

        Assert.Equal(
            new[] { First, Second }.Order(),
            rewrite.Calls.Where(c => c.ProfileId == "tier1").Select(c => c.Input).Order());
        Assert.Equal(
            [First + "\n\n" + Second, Third],
            rewrite.Calls.Where(c => c.ProfileId == "tier2").Select(c => c.Input));
        Assert.Equal("tier2[" + First + "\n\n" + Second + "]\n\ntier2[" + Third + "]", clipboard.Text);
    }

    // Stop 4 and Panel 4: when the step rewriting fails, each block keeps
    // what it showed, and that is what stays on the clipboard.
    [Fact]
    public async Task WhenTheStepRewritingFailsEachBlockKeepsWhatItShowed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService(failing: "tier2");
        StreamingHost host = StepsHost(whileSpeaking: true, ("tier1", 0, Min), ("tier2", RawTake.Length, Min));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);
        string shown = "tier1[" + First + "]\n\ntier1[" + Second + "]\n\n" + Third;

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 4, ct, readsBeforeStop: shown);

        Assert.Equal([shown], clipboard.Texts);
    }

    // Stop 3: with "Rewrite while I speak" off, nothing leaves while the
    // person speaks; the raw text goes to the clipboard, then the highest
    // step reached rewrites the whole of it and replaces it.
    [Fact]
    public async Task WithRewriteWhileSpeakingOffTheHighestStepRewritesTheWholeTextAtStop()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        StreamingHost host = StepsHost(whileSpeaking: false, ("tier1", 0, Min), ("tier2", AfterSecond, Min));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 0, ct);

        Assert.Equal([("tier2", RawTake)], rewrite.Calls);
        Assert.Equal([RawTake, "tier2[" + RawTake + "]"], clipboard.Texts);
    }

    // Stop 4: with it off, a failed rewrite leaves the raw text on the
    // clipboard.
    [Fact]
    public async Task WithRewriteWhileSpeakingOffAFailedRewriteLeavesTheRawText()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService(failing: "tier1");
        StreamingHost host = StepsHost(whileSpeaking: false, ("tier1", 0, Min));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 0, ct);

        Assert.Equal([RawTake], clipboard.Texts);
    }

    // Page 4: below the first step's "Start at", nothing is sent and the take
    // stays as said.
    [Fact]
    public async Task ATakeThatNeverReachesTheFirstStepStaysAsSaid()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        StreamingHost host = StepsHost(whileSpeaking: true, ("tier1", 1_000_000, Min));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 0, ct);

        Assert.Empty(rewrite.Calls);
        Assert.Equal([RawTake], clipboard.Texts);
    }

    // Steps (profile id, from, block size), the first from 0 to rewrite from\r\n    // the beginning. The profiles are the two the
    // settings hold, renamed.
    private static StreamingHost StepsHost(
        bool whileSpeaking, params (string ProfileId, int FromChars, int BlockChars)[] steps)
    {
        StreamingHost host = RewritingHost(batchRewriting: whileSpeaking);
        host.Llm.Profiles[0].Id = "tier1";
        host.Llm.Profiles[1].Id = "tier2";
        host.Llm.Dictation.Steps = steps
            .Select(s => new RewriteStep
            {
                ProfileId = s.ProfileId,
                FromChars = s.FromChars,
                BlockChars = s.BlockChars,
            })
            .ToList();
        return host;
    }
    private static TranscriptionEngine NewEngine(
        StreamingHost host, SequencedBackend backend, IRewriteService rewrite,
        out RecordingClipboardWriter clipboard)
    {
        clipboard = new RecordingClipboardWriter();
        return new TranscriptionEngine(
            host,
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 3),
            clipboard,
            rewrite,
            captureTail: TestCaptureTail);
    }

    // Starts a take, waits for the backend to transcribe the three utterances,
    // for the rewrite service to receive callsBeforeStop requests and, when
    // given, for the take to read readsBeforeStop, and only then presses Stop.
    private static async Task RunTakeAsync(
        TranscriptionEngine engine, SequencedBackend backend, TaggingRewriteService rewrite,
        int callsBeforeStop, CancellationToken ct, string? readsBeforeStop = null)
    {
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StatusChanged += status =>
        {
            if (status == Loc.Get("Status_Ready")) ready.TrySetResult();
        };
        engine.TranscriptionFinished += outcome => finished.TrySetResult(outcome);
        engine.TranscriptChanged += snapshot =>
        {
            if (snapshot.Text == readsBeforeStop) reads.TrySetResult();
        };

        Assert.Equal(ToggleResult.Started, engine.RequestToggle());
        await backend.AllCallsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        if (callsBeforeStop > 0)
            await rewrite.WaitForCalls(callsBeforeStop).WaitAsync(TimeSpan.FromSeconds(5), ct);
        if (readsBeforeStop is not null)
            await reads.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle());

        Assert.Equal(
            TranscriptionOutcome.ClipboardOnly,
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    // Records every request with its profile and answers "<profile>[<text>]",
    // so the delivered text shows which step rewrote each piece and how the
    // pieces were joined. A profile named failing answers nothing.
    private sealed class TaggingRewriteService(string? failing = null) : IRewriteService
    {
        private readonly ConcurrentQueue<(string ProfileId, string Input)> _calls = new();
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource Done)> _waiters = new();

        public IReadOnlyList<(string ProfileId, string Input)> Calls => _calls.ToArray();

        public Task WaitForCalls(int count)
        {
            lock (_gate)
            {
                if (_calls.Count >= count) return Task.CompletedTask;
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, done));
                return done.Task;
            }
        }

        public GenerationResult Rewrite(string text, RewriteProfile profile)
        {
            lock (_gate)
            {
                _calls.Enqueue((profile.Id, text));
                foreach (var waiter in _waiters.Where(w => _calls.Count >= w.Count))
                    waiter.Done.TrySetResult();
            }
            if (profile.Id == failing) return new GenerationResult(null, 1, 0, 0, 0, 0, 0);
            return new GenerationResult(profile.Id + "[" + text + "]", 1, 0, 0, 0, 0, 0);
        }

        public GenerationResult RewriteParagraph(string paragraph, CancellationToken cancellationToken) => default;

        public GenerationResult RewriteSentence(string sentence, CancellationToken cancellationToken) => default;
    }
}
