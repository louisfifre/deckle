#pragma once

#include "selector.h"

namespace deckle::rules
{
    // Whether `element` is the target of `selector`: it matches the last
    // matcher of the path, and its ancestors match the others, walking up with
    // VisualTreeHelper. Reads only; call it on the element's UI thread.
    //
    // Adapted from Windhawk's windows-11-taskbar-styler mod, GPL-3.0 (its
    // element matching walks parents the same way and backtracks over '*').
    // See NOTICE.md.
    bool MatchesSelector(winrt::Windows::UI::Xaml::FrameworkElement const& element, const Selector& selector);

    // The visual state group `name` of `element`, or null.
    winrt::Windows::UI::Xaml::VisualStateGroup FindVisualStateGroup(
        winrt::Windows::UI::Xaml::FrameworkElement const& element, std::wstring_view name);
}
