using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Deckle.App;

// The text field. A RichEditBox rather than a TextBox (Louis, 2026-11-14): a
// transcript arrives utterance by utterance, and a RichEditBox inserts at the
// end of its document without being handed the whole text again. Its append
// still costs more as the text grows; TranscribeRewriteWindow.Feed.cs groups
// the utterances for that reason. The field
// holds plain text only: formatting accelerators are off and a paste drops
// the source's formatting.
public sealed partial class TranscribeRewriteWindow
{
    // RichEdit ends its story with one paragraph mark of its own, which is not
    // part of the person's text.
    private string ReadText()
    {
        Editor.Document.GetText(TextGetOptions.UseCrlf, out string text);
        return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text;
    }

    // A read-only RichEditBox refuses programmatic edits too, so a run writes
    // through this gate while the person cannot type.
    private void WriteText(string text)
        => Programmatic(() => Editor.Document.SetText(TextSetOptions.None, text));

    private void AppendText(string piece)
        => Programmatic(() =>
        {
            ITextRange end = Editor.Document.GetRange(0, 0);
            end.EndOf(TextRangeUnit.Story, false);
            end.Text = piece;
        });

    private void Programmatic(Action edit)
    {
        bool readOnly = Editor.IsReadOnly;
        Editor.IsReadOnly = false;
        try { edit(); }
        finally { Editor.IsReadOnly = readOnly; }
    }

    // Not during a run: the commands are locked anyway, and reading the whole
    // text back at each append would add a cost that grows with the text.
    // EndRun updates them once.
    private void OnEditorTextChanged(object sender, RoutedEventArgs e)
    {
        if (!_running) UpdateCommands();
    }

    // Handled before the await: the field must not paste the rich version
    // while the plain one is read.
    private async void OnEditorPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true;
        try
        {
            DataPackageView view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.Text)) return;
            string text = await view.GetTextAsync();
            ITextSelection selection = Editor.Document.Selection;
            selection.SetText(TextSetOptions.None, text);
            selection.Collapse(false);
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "paste", "exception", ex.GetType().Name, ex.Message);
        }
    }
}
