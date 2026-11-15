using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// One button of the row: where it goes. Key tells two links apart when the row
// is set again (the remote's button comes and goes with the readings), so a
// button that stays keeps its focus.
internal sealed record OpenInLink(string Key, string Glyph, string Label, Action Invoke, string? Tooltip = null);

internal sealed partial class OpenInRow : UserControl
{
    private List<string> _keys = [];
    private readonly List<Button> _buttons = [];

    public OpenInRow()
    {
        InitializeComponent();
        TitleText.Text = ProjectsText.Get("Project_OpenIn");
    }

    // The labels of the buttons in order, for the self-check.
    public IReadOnlyList<string> LabelsForReview => [.. _buttons.Select(b => (string)AutomationProperties.GetName(b))];

    public void Apply(IReadOnlyList<OpenInLink> links)
    {
        List<string> keys = [.. links.Select(l => l.Key)];
        if (keys.SequenceEqual(_keys)) return;
        _keys = keys;

        _buttons.Clear();
        Links.Children.Clear();
        foreach (OpenInLink link in links)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = link.Glyph, FontSize = 16 },
                        new TextBlock { Text = link.Label, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            AutomationProperties.SetName(button, link.Label);
            if (link.Tooltip is not null) ToolTipService.SetToolTip(button, link.Tooltip);
            Action invoke = link.Invoke;
            button.Click += (_, _) => invoke();
            _buttons.Add(button);
            Links.Children.Add(button);
        }
    }
}
