using System.Collections.Concurrent;
using Deckle.Catalog;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;
using Xunit;
using static Deckle.Transcription.Tests.LiveTranscriptionPipelineTests;

namespace Deckle.Transcription.Tests;

// A take with no active profile and a length ladder opens its batch rewriting
// when the raw text first reaches a step, not at Stop. The engine is driven
// through the seams it already has: a scripted microphone and a scripted
// backend stand in for the model, so each scripted utterance reaches the
// engine's UtteranceTranscribed path exactly as a spoken one does.
[Trait("Category", "integration")]
public sealed class LengthStepBatchRewriteTests
{
    // Each utterance is longer than the minimum batch threshold (200) and ends
    // on a silence, so each one closes a batch of its own.
    private static readonly string First = Repeat("Je commence par le premier sujet de cette dictée. ");
    private static readonly string Second = Repeat("Puis je passe au second sujet, qui est différent. ");
    private static readonly string Third = Repeat("Enfin je termine par un dernier sujet, assez court. ");

    private static string Repeat(string sentence) =>
        string.Concat(Enumerable.Repeat(sentence, 5)).Trim();

    private static readonly string RawTake = First + "\n\n" + Second + "\n\n" + Third;

    [Fact]
    public async Task TheRunStartsAtTheFirstStepWhileTheSpeakerIsStillRecording()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new RecordingRewriteService();
        // The first step is reached by the second utterance, not by the first.
        StreamingHost host = LadderHost(("tier1", First.Length + 1));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out _);

        // Two requests left before Stop: the replayed first two utterances.
        // Without the feature nothing is sent until Stop and this wait times out.
        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 2, ct);

        Assert.Equal(
            new[] { First, Second, Third }.Order(),
            rewrite.Calls.Select(c => c.Input).Order());
        Assert.All(rewrite.Calls, call => Assert.Equal("tier1", call.ProfileId));
    }

    [Fact]
    public async Task TheReplayKeepsTheUtteranceOrderAndTheSilenceFlags()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new RecordingRewriteService();
        StreamingHost host = LadderHost(("tier1", First.Length + 1));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 2, ct);

        // The same text as a run that was there from the first utterance
        // (BatchRewritingRewritesEachBatchAndDeliversTheirJoinedRewrites): the
        // replayed utterances come in order, and each silence flag still makes
        // a paragraph break at the seam. One raw copy, then one assembled copy.
        Assert.Equal(2, clipboard.WriteCount);
        Assert.Equal("[" + First + "]\n\n[" + Second + "]\n\n[" + Third + "]", clipboard.Text);
    }

    [Fact]
    public async Task AFinalLengthOnAnotherStepDiscardsTheRunAndRewritesTheWholeTake()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new RecordingRewriteService();
        // Tier 1 is reached by the first two utterances; the whole take
        // reaches tier 2 exactly.
        StreamingHost host = LadderHost(("tier1", First.Length + 1), ("tier2", RawTake.Length));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        // Three batches leave during the take, the third one live after the
        // run opened; the wait keeps their late arrival out of the count below.
        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 3, ct);

        // The three batches sent during the take with tier 1 are dropped at
        // Stop; the whole take is then rewritten with the profile of its final
        // length, as when batching is off.
        Assert.Equal(
            new[] { First, Second, Third }.Order(),
            rewrite.Calls.Where(c => c.ProfileId == "tier1").Select(c => c.Input).Order());
        Assert.Equal(
            [RawTake],
            rewrite.Calls.Where(c => c.ProfileId == "tier2").Select(c => c.Input));
        Assert.Equal(4, rewrite.Calls.Count);
        Assert.Equal(2, clipboard.WriteCount);
        Assert.Equal("[" + RawTake + "]", clipboard.Text);
    }

    [Fact]
    public async Task ATakeThatNeverReachesTheFirstStepCreatesNoRun()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var rewrite = new RecordingRewriteService();
        StreamingHost host = LadderHost(("tier1", 1_000_000));
        var backend = new SequencedBackend([TestInference, First, Second, Third]);
        using var engine = NewEngine(host, backend, rewrite, out RecordingClipboardWriter clipboard);

        await RunTakeAsync(engine, backend, rewrite, callsBeforeStop: 0, ct);

        Assert.Empty(rewrite.Calls);
        Assert.Equal(1, clipboard.WriteCount);
        Assert.Equal(RawTake, clipboard.Text);
    }

    // No active profile, batching on, rewriting on, and a ladder of steps
    // (profile id, first length). Profiles are the two the settings hold.
    private static StreamingHost LadderHost(params (string ProfileId, int FromChars)[] steps)
    {
        StreamingHost host = RewritingHost(batchRewriting: true);
        host.Llm.ActiveProfileId = null;
        host.Llm.ActiveProfileName = null;
        host.Llm.Profiles[0].Id = "tier1";
        host.Llm.Profiles[1].Id = "tier2";
        host.Llm.LengthSwitch = new LengthSwitchSettings
        {
            Enabled = true,
            Steps = steps
                .Select(s => new LengthStep { FromChars = s.FromChars, ProfileId = s.ProfileId })
                .ToList(),
        };
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

    // Starts a take, waits for the backend to transcribe the three utterances
    // and for the rewrite service to receive callsBeforeStop requests, and only
    // then presses Stop.
    private static async Task RunTakeAsync(
        TranscriptionEngine engine, SequencedBackend backend, RecordingRewriteService rewrite,
        int callsBeforeStop, CancellationToken ct)
    {
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StatusChanged += status =>
        {
            if (status == Loc.Get("Status_Ready")) ready.TrySetResult();
        };
        engine.TranscriptionFinished += outcome => finished.TrySetResult(outcome);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        await backend.AllCallsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        if (callsBeforeStop > 0)
            await rewrite.WaitForCalls(callsBeforeStop).WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle(shouldPaste: false));

        Assert.Equal(
            TranscriptionOutcome.ClipboardOnly,
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    // Records every request with its profile and brackets the text, so the
    // delivered text shows what was rewritten and how it was joined.
    private sealed class RecordingRewriteService : IRewriteService
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
            return new GenerationResult("[" + text + "]", 1, 0, 0, 0, 0, 0);
        }

        public GenerationResult RewriteParagraph(string paragraph, CancellationToken cancellationToken) => default;

        public GenerationResult RewriteSentence(string sentence, CancellationToken cancellationToken) => default;
    }
}
