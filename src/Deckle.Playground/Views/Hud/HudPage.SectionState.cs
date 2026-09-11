using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Playground;

public sealed partial class HudPage
{
    private readonly Dictionary<string, bool> _sectionExpansion = new();

    // Preserve the native Expander template and all user-triggered animations.
    // Its initial ExpandDown storyboard (not a VisualTransition) must finish
    // before the loaded control is presented. GoToState(false) cannot do this.
    // Inspect the template root via public APIs; no named internal template part
    // or replacement template is required. Loaded also covers cached page returns.
    private static void SettleInitialSectionState(object sender, RoutedEventArgs args)
    {
        if (sender is not Expander section) return;
        section.ApplyTemplate();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(section); i++)
        {
            if (VisualTreeHelper.GetChild(section, i) is not FrameworkElement root) continue;
            foreach (var group in VisualStateManager.GetVisualStateGroups(root))
                group.CurrentState?.Storyboard?.SkipToFill();
        }
    }
}
