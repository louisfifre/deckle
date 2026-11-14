using Deckle.Composition;
using Deckle.Core;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App;

// The states the self-check photographs (App.SelfCheck.cs), set without a
// file or a provider: the window as its spec describes it with a text, while
// a transcription works, and with a failure notice.
public sealed partial class TranscribeRewriteWindow
{
    internal const string EmptyState = "empty";

    // files-start: the moment after the choice; files-full: forty files with
    // long names; files-narrow: the list at the window's minimum width;
    // file-from-list: one file of the list opened in the field.
    internal static readonly string[] CaptureStates =
        ["text", "transcribing", "rewriting", "failed", "files", "files-start", "files-full", "files-narrow", "file-from-list"];

    // A long, realistic recording name, to see how a line holds it.
    private const string LongName =
        "2026-11-12 Client workshop with the product team about the spring roadmap, recording 3";

    // The list of several files, every state a file can be in.
    private static readonly (string Name, string Duration, FileTranscriptionState State, bool RewriteFailed, bool HasText)[] SampleFiles =
    [
        ("Team meeting.m4a", "48:12", FileTranscriptionState.Done, false, true),
        ("Interview, part 1.mp3", "1:12:40", FileTranscriptionState.Done, true, true),
        ("Interview, part 2.mp3", "1:03:05", FileTranscriptionState.Transcribing, false, false),
        ("Voice memo.wav", "2:31", FileTranscriptionState.Waiting, false, false),
        ("Corrupted.ogg", "", FileTranscriptionState.TranscriptionFailed, false, false),
    ];

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
        ResetFilesRun();
        HideNotice();
        _files.Clear();
        ShowField();
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
        ResetFilesRun();
        HideNotice();
        ResizeForCapture(state == "files-narrow" ? 520 : 640);
        switch (state)
        {
            case "files" or "files-narrow":
                ShowSampleFiles();
                return;
            case "files-start":
                ShowGeneratedFiles(5, running: 0, done: 0);
                return;
            case "files-full":
                ShowGeneratedFiles(40, running: 8, done: 7);
                return;
            case "file-from-list":
                ShowSampleFiles();
                OpenFile(_files[0]);
                return;
        }
        _files.Clear();
        ShowField();
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

    private void ShowSampleFiles()
    {
        _files.Clear();
        string folder = Path.Combine(Path.GetTempPath(), "Recordings");
        foreach (var sample in SampleFiles)
        {
            var item = new FileTranscriptionItem(Path.Combine(folder, sample.Name)) { DurationText = sample.Duration };
            if (sample.HasText) item.Text = SampleText;
            if (sample.State == FileTranscriptionState.Done)
                item.OutputPath = Path.ChangeExtension(item.Path, ".txt");
            item.RewriteFailed = sample.RewriteFailed;
            item.State = sample.State;
            _files.Add(item);
        }
        ShowFiles();
    }

    // count files named after LongName: the first `done` saved, the next one
    // transcribing when running > done, the rest waiting with no duration yet.
    private void ShowGeneratedFiles(int count, int running, int done)
    {
        _files.Clear();
        string folder = Path.Combine(Path.GetTempPath(), "Recordings");
        for (int i = 0; i < count; i++)
        {
            var item = new FileTranscriptionItem(Path.Combine(folder, $"{LongName} - {i + 1:00}.m4a"));
            if (i < done)
            {
                item.DurationText = "1:0" + (i % 10) + ":12";
                item.Text = SampleText;
                item.OutputPath = Path.ChangeExtension(item.Path, ".txt");
                item.State = FileTranscriptionState.Done;
            }
            else if (i == done && running > done)
            {
                item.DurationText = "58:40";
                item.State = FileTranscriptionState.Transcribing;
            }
            else if (i == 0)
            {
                item.State = FileTranscriptionState.Transcribing;
            }
            _files.Add(item);
        }
        ShowFiles();
        BeginFilesRun(rewriting: false);
    }

    private void ResizeForCapture(int widthDip)
    {
        double scale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(widthDip * scale), (int)(480 * scale)));
    }
}
