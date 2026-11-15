using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// The last place of a category's grid when it holds more projects than the
// grid has places: "+ 4 more", which opens the category's page (spec, Home).
// No board draws it, so it is a plain standard button given the cards' size and
// corners, which keeps it a control Windows already knows how to focus, press
// and read aloud.
internal sealed class MoreTile : Button
{
    public MoreTile(CardGridPlan plan, string categoryName, Action open)
    {
        Content = new TextBlock { Text = plan.MoreText, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        MinHeight = (double)Application.Current.Resources["ProjectsCardMinHeight"];
        CornerRadius = (Microsoft.UI.Xaml.CornerRadius)Application.Current.Resources["OverlayCornerRadius"];
        AutomationProperties.SetName(this, ProjectsText.Format("Home_MoreTile_Format", plan.MoreCount, categoryName));
        Click += (_, _) => open();
    }
}
