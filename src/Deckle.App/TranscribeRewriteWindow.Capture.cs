using Deckle.Composition;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App;

// The states the self-check photographs (App.SelfCheck.cs), set without a
// file or a provider: the window as its spec describes it with a text, while
// a transcription works, and with a failure notice.
public sealed partial class TranscribeRewriteWindow
{
    internal const string EmptyState = "empty";

    internal static readonly string[] CaptureStates = ["text", "transcribing", "rewriting", "failed"];

    // A neutral sample, long enough to wrap and to show the field's scroll.
    private const string SampleText =
        "The meeting opened on the schedule for the spring release. The team agreed to move the "
        + "review to Thursday, so the notes can be read before it.\r\r"
        + "Two points stay open: who owns the test plan, and whether the demo runs on the new build "
        + "or the current one. Both come back at the next meeting.";

    // The measure Louis asked for before trusting the field with a long file
    // (2026-11-14): three hours of speech, about 1,800 utterances of 15 words,
    // appended one by one as a transcription does. Each append is timed until
    // the dispatcher has run its layout and reached Low priority, so the time
    // includes the field's relayout. A transcription brings an utterance every
    // second or so: a worst case well under one frame (16 ms) never stutters.
    internal const int LongTranscriptUtterances = 1_800;

    internal async Task<string> MeasureLongTranscriptAsync()
    {
        if (_running) EndRun();
        HideNotice();
        WriteText("");
        BeginRun(ProcessingVariant.Transcribing);

        const string utterance =
            "and then we went over the figures again to see where the gap came from";
        // The first and last tenth apart: a cost that grows with the text
        // shows as a last tenth slower than the first.
        int tenth = LongTranscriptUtterances / 10;
        long worstTicks = 0, totalTicks = 0, firstTicks = 0, lastTicks = 0;
        for (int i = 0; i < LongTranscriptUtterances; i++)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            AppendText(i == 0 ? utterance : (i % 12 == 0 ? "\r\r" : " ") + utterance);
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => settled.TrySetResult());
            await settled.Task;
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            totalTicks += elapsed;
            worstTicks = Math.Max(worstTicks, elapsed);
            if (i < tenth) firstTicks += elapsed;
            else if (i >= LongTranscriptUtterances - tenth) lastTicks += elapsed;
        }

        double ToMs(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        string result = $"{LongTranscriptUtterances} utterances, {ReadText().Length:N0} chars: "
            + $"mean {ToMs(totalTicks) / LongTranscriptUtterances:F2} ms, "
            + $"first tenth {ToMs(firstTicks) / tenth:F2} ms, last tenth {ToMs(lastTicks) / tenth:F2} ms, "
            + $"worst {ToMs(worstTicks):F1} ms per append";
        EndRun();
        UpdateCommands();
        return result;
    }

    internal void ShowStateForCapture(string state)
    {
        if (_running) EndRun();
        HideNotice();
        switch (state)
        {
            case EmptyState:
                WriteText("");
                break;
            case "text":
                WriteText(SampleText);
                break;
            case "transcribing":
                WriteText(SampleText);
                BeginRun(ProcessingVariant.Transcribing);
                break;
            case "rewriting":
                WriteText(SampleText);
                BeginRun(ProcessingVariant.Rewriting);
                break;
            case "failed":
                WriteText(SampleText);
                ShowNotice(InfoBarSeverity.Error,
                    "TranscribeRewrite_RewriteFailed_Title", "TranscribeRewrite_RewriteFailed_Body",
                    withSettings: true);
                break;
        }
        UpdateCommands();
    }
}
