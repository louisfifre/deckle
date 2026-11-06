using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Deckle.Settings;

// ── Click away to leave a field ───────────────────────────────────────────────
//
// A press on an empty part of the window takes the focus out of the page
// field being edited, as Windows Settings does: the caret goes away and a
// field that commits on LostFocus commits. One handler on the window covers
// every page, present and future, instead of one per page (the April 2026
// version lived in two pages and was lost with them). The search box has its
// own release (SettingsWindow.Search.cs), which hands focus to the same frame.
//
// Only presses no control handled reach here: a Button, a TextBox, a Slider,
// a ToggleSwitch or an expander header marks its own press handled, so
// clicking one keeps its usual focus behaviour. What remains is the empty
// space, a label or a card's text.
public sealed partial class SettingsWindow
{
    private void InstallFocusDismiss()
    {
        RootGrid.PointerPressed += OnRootPointerPressedOutsideField;
    }

    private void OnRootPointerPressedOutsideField(object sender, PointerRoutedEventArgs e)
    {
        if (Content.XamlRoot is null) return;
        if (FocusManager.GetFocusedElement(Content.XamlRoot) is not DependencyObject focused) return;
        if (ReferenceEquals(focused, PageFrame) || !IsWithin(focused, PageFrame)) return;

        PageFrame.Focus(FocusState.Programmatic);
    }
}
