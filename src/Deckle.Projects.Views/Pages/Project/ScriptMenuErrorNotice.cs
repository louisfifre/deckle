using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Projects.Views;

// "The script menu could not be read" and the last lines of what it said. The
// lines are the project's own text: they are shown here and never written to a
// log. They are selectable, so the person can copy them into a search.
//
// The lines are set in a monospaced face because they are a terminal's output.
// No theme resource names a monospaced font (the rest of Deckle writes
// "Consolas" where it needs one, as the log window does), so it is set here
// once.
internal sealed class ScriptMenuErrorNotice : UserControl
{
    private readonly TextBlock _lines;

    public ScriptMenuErrorNotice()
    {
        TextBlock title = Themed.Text("TextFillColorPrimaryBrush", "BodyStrongTextBlockStyle");
        title.Text = ProjectsText.Get("Pinned_MenuError");

        _lines = Themed.Text("TextFillColorSecondaryBrush", "CaptionTextBlockStyle");
        _lines.FontFamily = new FontFamily("Consolas");
        _lines.TextWrapping = TextWrapping.Wrap;
        _lines.IsTextSelectionEnabled = true;

        Content = new StackPanel { Spacing = 4, Children = { title, _lines } };
    }

    public string Lines => _lines.Text;

    public void Show(string lastLines)
    {
        _lines.Text = lastLines.Trim();
        _lines.Visibility = string.IsNullOrWhiteSpace(lastLines) ? Visibility.Collapsed : Visibility.Visible;
    }
}
