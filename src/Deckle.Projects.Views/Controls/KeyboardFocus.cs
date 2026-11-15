using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Projects.Views;

// Where the keyboard goes when the control it is on switches off or goes away
// (Collapsed, disabled): left alone, the focus would drop to nowhere and the
// person would lose their place.
internal static class KeyboardFocus
{
    // Gives the focus to the nearest control around the element that the
    // keyboard can stop on: the Home card, or the list item that holds a
    // category row. Does nothing when there is none.
    public static void HandToOwner(DependencyObject element)
    {
        for (DependencyObject? e = VisualTreeHelper.GetParent(element); e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is Control { IsTabStop: true, IsEnabled: true } owner)
            {
                owner.Focus(FocusState.Programmatic);
                return;
            }
        }
    }
}
