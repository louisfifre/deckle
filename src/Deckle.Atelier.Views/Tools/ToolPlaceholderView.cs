using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Atelier.Views;

// The centre of a tool not built yet: its glyph, its name and one line that
// says so. It shows the folder by doing nothing, and puts nothing in the
// details panel. A tool's session replaces it with its own view.
internal sealed class ToolPlaceholderView : IAtelierToolView
{
    public ToolPlaceholderView(IAtelierTool tool)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
        };
        panel.Children.Add(new FontIcon
        {
            Glyph = tool.Glyph,
            FontSize = 32,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        panel.Children.Add(new TextBlock
        {
            Text = tool.Name,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = AtelierText.Get("Tool_Placeholder"),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Content = panel;
    }

    public FrameworkElement Content { get; }

    public Task ShowAsync(FolderPlace place, string? selection, CancellationToken cancellation) => Task.CompletedTask;
}
