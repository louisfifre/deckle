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
    // Each utterance is longer than the minimum passage size (200) and ends on
    // a silence, so each one closes a passage of its own.
    private static readonly string First = Repeat("Je commence par le premier sujet de cette dictée. ");
    private static readonly string Second = Repeat("Puis je passe au second sujet, qui est différent. ");
    private static readonly string Third = Repeat("Enfin je termine par un dernier sujet, assez court. ");

    private static string Repeat(string sentence) =>
        string.Concat(Enumerable.Repeat(sentence, 5)).Trim();

    private static readonly string RawTake = First + "\n\n" + Second + "\n\n" + Third;

    // The raw length once the second utterance is in.
    private static readonly int AfterSecond = First.Length + 2 + Second.Length;

    // Panel 1 and 2: the first step starts at a length the second utterance
    // reaches; the passage closed before it leaves then, while the speaker is
    // still recording.
    [Fact]
    public async Task ReachingTheFirstStreamingStepSendsThePassagesClosedBeforeIt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        StreamingHost host = StepsHost(startAt: First.Length + 1, ("tier1", 0, true));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        // Without the streaming step nothing is sent before Stop and this wait
        // times out.
        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 2, ct);

        Assert.Equal(
            new[] { First, Second, Third }.Order(),
            rewrite.Calls.Select(c => c.Input).Order());
        Assert.All(rewrite.Calls, call => Assert.Equal("tier1", call.ProfileId));
        Assert.Equal("tier1[" + First + "]\n\ntier1[" + Second + "]\n\ntier1[" + Third + "]", clipboard.Text);
        Assert.InRange(clipboard.WriteCount, 1, 2);
    }

    // Panel 3 and Page 6: a higher streaming step rewrites the beginning again,
    // from the raw text, with its own profile.
    [Fact]
    public async Task AHigherStreamingStepRewritesTheBeginningAgainFromTheRawText()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        StreamingHost host = StepsHost(startAt: null, ("tier1", 0, true), ("tier2", AfterSecond, true));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        // tier1 on the first passage, then tier2 on all three.
        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 4, ct);

        Assert.Equal([First], rewrite.Calls.Where(c => c.ProfileId == "tier1").Select(c => c.Input));
        Assert.Equal(
            new[] { First, Second, Third }.Order(),
            rewrite.Calls.Where(c => c.ProfileId == "tier2").Select(c => c.Input).Order());
        Assert.Equal("tier2[" + First + "]\n\ntier2[" + Second + "]\n\ntier2[" + Third + "]", clipboard.Text);
    }

    // Stop 3: the highest step reached is "at Stop", after a streaming step.
    // The passage version goes to the clipboard first; the step's profile then
    // rewrites the whole raw text and replaces it.
    [Fact]
    public async Task AStepAtStopRewritesTheWholeRawTextAfterThePassageVersion()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        StreamingHost host = StepsHost(startAt: null, ("tier1", 0, true), ("tier2", RawTake.Length, false));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);
        string passages = "tier1[" + First + "]\n\ntier1[" + Second + "]\n\ntier1[" + Third + "]";

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 3, ct, readsBeforeStop: passages);

        Assert.Equal([RawTake], rewrite.Calls.Where(c => c.ProfileId == "tier2").Select(c => c.Input));
        Assert.Equal([passages, "tier2[" + RawTake + "]"], clipboard.Texts);
    }

    // Stop 5: when that profile fails, the passage version stays on the
    // clipboard.
    [Fact]
    public async Task WhenTheStepAtStopFailsThePassageVersionStays()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService(failing: "tier2");
        StreamingHost host = StepsHost(startAt: null, ("tier1", 0, true), ("tier2", RawTake.Length, false));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);
        string passages = "tier1[" + First + "]\n\ntier1[" + Second + "]\n\ntier1[" + Third + "]";

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 3, ct, readsBeforeStop: passages);

        Assert.Equal([passages], clipboard.Texts);
    }

    // Stop 4: a step "at Stop" with no streaming step before it. Nothing
    // leaves while the person speaks; the raw text goes to the clipboard,
    // then the whole rewrite replaces it.
    [Fact]
    public async Task AStepAtStopWithNoStreamingStepBeforeItSendsNothingWhileSpeaking()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        StreamingHost host = StepsHost(startAt: null, ("tier1", 0, false));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 0, ct);

        Assert.Equal([("tier1", RawTake)], rewrite.Calls);
        Assert.Equal([RawTake, "tier1[" + RawTake + "]"], clipboard.Texts);
    }

    // Page 3: below the first step's "Start at", nothing is sent and the take
    // stays as said.
    [Fact]
    public async Task ATakeThatNeverReachesTheFirstStepStaysAsSaid()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new TaggingRewriteService();
        StreamingHost host = StepsHost(startAt: 1_000_000, ("tier1", 0, true));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 0, ct);

        Assert.Empty(rewrite.Calls);
        Assert.Equal([RawTake], clipboard.Texts);
    }

    // Steps (profile id, from, streaming); startAt sets the first step's
    // "Start at" (null: from the beginning). The profiles are the two the
    // settings hold, renamed.
    private static StreamingHost StepsHost(int? startAt, params (string ProfileId, int FromChars, bool Streaming)[] steps)
    {
        StreamingHost host = RewritingHost(batchRewriting: false);
        host.Llm.Profiles[0].Id = "tier1";
        host.Llm.Profiles[1].Id = "tier2";
        host.Llm.Dictation.StartAtLength = startAt is not null;
        host.Llm.Dictation.RewriteWhileSpeaking = steps.Any(s => s.Streaming);
        host.Llm.Dictation.Steps = steps
            .Select((s, i) => new RewriteStep
            {
                ProfileId = s.ProfileId,
                FromChars = i == 0 && startAt is { } at ? at : s.FromChars,
                BlockChars = DictationRewriteSettings.MinBlockChars,
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
