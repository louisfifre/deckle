using System.Text;
using Deckle.Transcription;
using Microsoft.UI.Dispatching;

namespace Deckle.App;

// The transcript as it comes, grouped. One append to the RichEditBox costs
// more as the text grows: the self-check's "long" view measured about 3 ms per
// append in the first tenth of three hours of speech and about 32 ms in the
// last (Debug build, 2026-11-14). The field therefore takes the utterances in
// groups, at most four times a second, whatever pace the engine keeps: at the
// end of a long file that is under a seventh of the UI thread, and the text
// still visibly grows.
public sealed partial class TranscribeRewriteWindow
{
    private static readonly TimeSpan FeedInterval = TimeSpan.FromMilliseconds(250);

    private readonly StringBuilder _pendingUtterances = new();
    private DispatcherQueueTimer? _feedTimer;
    private bool _feedStarted;
    private bool _feedParagraph;

    private void StartUtteranceFeed()
    {
        _pendingUtterances.Clear();
        _feedStarted = false;
        _feedParagraph = false;
        _feedTimer ??= CreateFeedTimer();
        _feedTimer.Start();
    }

    // The last group goes in before the final text replaces it all, so
    // nothing is lost if the run ends between two ticks.
    private void StopUtteranceFeed()
    {
        _feedTimer?.Stop();
        FlushUtterances();
    }

    // On the UI thread (marshalled by the caller).
    private void QueueUtterance(TranscribedUtterance utterance)
    {
        if (!_running) return;
        if (_feedStarted) _pendingUtterances.Append(_feedParagraph ? "\r\r" : " ");
        _pendingUtterances.Append(utterance.Text);
        _feedStarted = true;
        _feedParagraph = utterance.EndedOnSilence;
    }

    private void FlushUtterances()
    {
        if (_pendingUtterances.Length == 0) return;
        AppendText(_pendingUtterances.ToString());
        _pendingUtterances.Clear();
    }

    private DispatcherQueueTimer CreateFeedTimer()
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = FeedInterval;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => FlushUtterances();
        return timer;
    }
}
