using System.Text;

namespace Deckle.Input.Autocorrect.Tracking;

// Pure state machine over the decoded keystroke stream: accumulates the word
// under the caret, commits it on a boundary, and harvests the « typed wrong,
// backspaced, retyped » gesture as a WordEdit. No OS calls, no threads — time
// rides in on the keystrokes, effects go out as events, so every quality
// decision is testable keystroke by keystroke (mirrors the trackpad recognizer).
//
// The edit window is the subtle part. The autocorrect observes and repairs
// AFTER a word commits; the same window that lets the engine fix a word
// also lets the tracker watch the user fix it. After a commit the screen holds
// « word » + boundary; the first Backspace eats the boundary and RE-OPENS the
// committed word as the live buffer, with previousWord reverted one step (a
// two-deep memory) so the re-commit lands in the same slot. Further backspaces
// shorten it; backspacing past its start means the caret left what we model —
// a hard reset. Re-committing a different word emits WordEdit(original, new).
public sealed class TypedWordTracker
{
    private const int BufferCap = 64;

    private readonly StringBuilder _buffer = new();

    // The token committed just before the live word on this surface, with no
    // intervening reset — surfaced as WordCommit.PreviousWord. Null after a reset.
    private string? _previousWord;

    // ── Edit-window state ──
    // Open from a commit until a keystroke kind other than Backspace/Text
    // intervenes (a reset of any flavor closes it too). While open, the first
    // empty-buffer Backspace re-opens _lastCommittedWord for re-editing.
    private bool _editWindowOpen;
    private bool _reopened;             // the live buffer currently holds a re-opened word
    private string? _lastCommittedWord; // the word the window can re-open
    private string? _wordBeforeLast;    // previousWord as it stood before _lastCommittedWord
    private string? _originalForEdit;   // compared against the re-commit to emit a WordEdit

    public Action<WordCommit>? WordCommitted;
    public Action<WordEdit>? WordEdited;
    public Action<ResetReason>? TrackerReset;

    /// <summary>The live buffer, for the CLI watch display.</summary>
    public string CurrentWord => _buffer.ToString();

    public void OnKeystroke(Keystroke k)
    {
        switch (k.Kind)
        {
            case KeystrokeKind.Text:
                foreach (char c in k.Text)
                    ProcessChar(c, k.TimestampMs);
                break;

            case KeystrokeKind.Backspace:
                ProcessBackspace();
                break;

            case KeystrokeKind.Enter:
                Reset(ResetReason.Enter);
                break;

            case KeystrokeKind.Tab:
            case KeystrokeKind.Navigation:
                Reset(ResetReason.Navigation);
                break;

            case KeystrokeKind.Escape:
                Reset(ResetReason.Escape);
                break;

            case KeystrokeKind.Shortcut:
                Reset(ResetReason.Shortcut);
                break;

            case KeystrokeKind.Delete:
                Reset(ResetReason.Delete);
                break;

            case KeystrokeKind.DeadKey:
                Reset(ResetReason.DeadKey);
                break;

            case KeystrokeKind.Other:
                break; // irrelevant to the buffer — no reset
        }
    }

    public void NotifyPointerInteraction() => Reset(ResetReason.PointerInteraction);

    public void NotifyFocusChanged() => Reset(ResetReason.FocusChanged);

    /// <summary>
    /// Aligns the tracker with a correction the engine just injected: the word
    /// on screen is now <paramref name="replacement"/>. The edit window and the
    /// previousWord chain must follow the screen, not the keystrokes, or the
    /// revert gesture would reopen the wrong text.
    /// </summary>
    public void ReplaceLastCommitted(string replacement)
    {
        if (!_editWindowOpen || _reopened || _lastCommittedWord is null) return;
        _lastCommittedWord = replacement;
        _previousWord = replacement;
    }

    /// <summary>
    /// Aligns the re-opened live buffer with text the engine just injected
    /// (correction revert): the screen now holds <paramref name="text"/> where
    /// the tracker had re-opened the corrected word.
    /// </summary>
    public void ReplaceReopenedBuffer(string text)
    {
        if (!_reopened) return;
        _buffer.Clear();
        _buffer.Append(text);
        _originalForEdit = null; // the revert is not a user edit to harvest
    }

    private void ProcessChar(char c, double timestampMs)
    {
        if (WordBoundaries.IsApostrophe(c))
        {
            ProcessApostrophe(timestampMs);
            return;
        }

        if (WordBoundaries.IsWordChar(c))
        {
            // Typing into a freshly committed slot (window open, not yet
            // re-opened) means the user moved on: a brand-new word, close the
            // window so this is not mistaken for a correction of the last one.
            if (_editWindowOpen && !_reopened)
                CloseEditWindow();

            _buffer.Append(c);
            if (_buffer.Length > BufferCap)
            {
                // BufferLimit is the only cap-overflow reason — see ResetReason.
                Reset(ResetReason.BufferLimit);
            }
            return;
        }

        // Any other char (space, punctuation, …) is a boundary.
        ProcessBoundary(c, timestampMs);
    }

    private void ProcessApostrophe(double timestampMs)
    {
        if (_buffer.Length > 0 && WordBoundaries.IsElisionPrefix(_buffer.ToString()))
        {
            _buffer.Append('\''); // normalized apostrophe, attached
            Commit('\'', timestampMs);
            return;
        }

        if (_buffer.Length > 0)
        {
            _buffer.Append('\''); // joins the buffer (« aujourd'hui »)
            return;
        }

        // Empty buffer: boundary noise — commits nothing, clears nothing, but
        // closes the edit window like any other empty-buffer boundary.
        if (_editWindowOpen)
            CloseEditWindow();
    }

    private void ProcessBoundary(char boundary, double timestampMs)
    {
        if (_buffer.Length == 0)
        {
            // Consecutive boundaries are noise; a boundary never clears the
            // previousWord context. It DOES push the committed word away from
            // the caret, so the re-open gesture no longer concerns it — a
            // Backspace here eats this extra boundary, not the commit's.
            if (_editWindowOpen)
                CloseEditWindow();
            return;
        }

        Commit(boundary, timestampMs);
    }

    private void ProcessBackspace()
    {
        if (_buffer.Length > 0)
        {
            _buffer.Length--; // drop the last char of the live word
            return;
        }

        // Empty buffer.
        if (_editWindowOpen && !_reopened && _lastCommittedWord is not null)
        {
            // First backspace after a commit: eat the boundary, re-open the
            // committed word as the live buffer, revert previousWord one step.
            _buffer.Append(_lastCommittedWord);
            _previousWord = _wordBeforeLast;
            _originalForEdit = _lastCommittedWord;
            _reopened = true;
            return;
        }

        if (_reopened)
        {
            // We re-opened a word and backspaced it all away; one more eats
            // past its start, into territory we no longer model — hard reset.
            // Navigation is the closest fit (caret left the modeled span);
            // BufferLimit stays reserved for cap overflow.
            Reset(ResetReason.Navigation);
            return;
        }

        // No edit window, empty buffer — backspace is plain noise.
    }

    // Commits the current buffer as a word, raises WordCommitted, and (when
    // re-editing) WordEdited. Opens a fresh edit window on the committed word.
    //
    // State lands BEFORE the events fire: a WordCommitted handler may react by
    // injecting a correction and calling ReplaceLastCommitted — it must find
    // the window already open, and nothing here may overwrite its realignment
    // afterward.
    private void Commit(char boundary, double timestampMs)
    {
        string word = _buffer.ToString();
        string? wordBeforeThis = _previousWord;
        bool wasReopened = _reopened;
        string? original = _originalForEdit;

        // Chain: this word becomes the previous one for whatever comes next.
        _previousWord = word;
        _buffer.Clear();

        // Open the edit window on this commit. Two-deep memory: the word before
        // THIS one is what previousWord reverts to if the window re-opens.
        _lastCommittedWord = word;
        _wordBeforeLast = wordBeforeThis;
        _originalForEdit = null;
        _editWindowOpen = true;
        _reopened = false;

        WordCommitted?.Invoke(new WordCommit(word, boundary, wordBeforeThis, timestampMs));

        if (wasReopened && original is not null && !string.Equals(original, word, StringComparison.Ordinal))
            WordEdited?.Invoke(new WordEdit(original, word, timestampMs));
    }

    private void CloseEditWindow()
    {
        _editWindowOpen = false;
        _reopened = false;
        _lastCommittedWord = null;
        _wordBeforeLast = null;
        _originalForEdit = null;
    }

    private void Reset(ResetReason reason)
    {
        _buffer.Clear();
        _previousWord = null;
        CloseEditWindow();
        TrackerReset?.Invoke(reason);
    }
}
