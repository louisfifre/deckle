namespace Deckle.App;

// What the auto-save of the Transcribe and rewrite window decides, apart from
// the window: given the field's text, does the open file's .txt need writing,
// and did the write succeed. The window keeps the timer, the notice and the
// log; this keeps the rule that a .txt only looked at is never rewritten.
internal sealed class OpenFileAutoSave(FileTranscriptionItem file, string savedText)
{
    // The field's text as it stood when it was last in step with the .txt: at
    // the opening, then after each write. RichEdit hands text back with its
    // own line breaks, so comparing with the file's text would rewrite a file
    // that was only looked at.
    private string _savedText = savedText;

    public enum Outcome { Unchanged, Written, Failed }

    public readonly record struct Result(Outcome Outcome, Exception? Error = null);

    // Writes the field's text over the file's .txt when it differs from the
    // text the field was last in step with. A write that cannot be made is
    // reported, not thrown, and leaves the item as it was.
    public Result Save(string fieldText)
    {
        if (file.OutputPath is not { } path) return new(Outcome.Unchanged);
        if (fieldText == _savedText) return new(Outcome.Unchanged);
        try
        {
            File.WriteAllText(path, fieldText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(Outcome.Failed, ex);
        }
        _savedText = fieldText;
        file.Text = fieldText;
        return new(Outcome.Written);
    }
}
