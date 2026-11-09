#pragma once

#include <string>
#include <string_view>
#include <vector>

namespace deckle::rules
{
    // A rule says which element it targets with a selector, the subset of the
    // Windhawk taskbar styler's target syntax that Deckle's rules use:
    //
    //   Taskbar.TaskbarFrame > Grid#RootGrid > Rectangle#BackgroundFill
    //   SystemTray.NotificationAreaIcons > * > SystemTray.NotifyIconView#NotifyItemIcon
    //   Taskbar.TaskListButton[2] > Grid@CommonStates
    //   Taskbar.ExperienceToggleButton[AutomationProperties.AutomationId=StartButton]
    //
    //   Type          a runtime class name; a bare name stands for the XAML
    //                 control of that name (Grid is Windows.UI.Xaml.Controls.Grid)
    //   #Name         the element's x:Name
    //   @Group        a visual state group the element has (rules with a
    //                 visualState apply only while that group is in that state)
    //   [N]           the element is the Nth child (1-based) of its parent
    //   [Prop=Value]  a property filter (see element_matcher.cpp for the list)
    //   >             the left element is the parent of the right one
    //   *             any chain of ancestors, none included
    //
    // The first element of a path may sit anywhere in the tree; it is not
    // anchored at the root. This file is plain C++ with no XAML in it, so the
    // parser can be tested from C# through the DeckleTaskbarDescribeSelector
    // export.
    //
    // Adapted from Windhawk's windows-11-taskbar-styler mod (ElementMatcherFromString
    // and AdjustTypeName), GPL-3.0, by m417z and the mod authors:
    // https://github.com/ramensoftware/windhawk-mods/blob/main/mods/windows-11-taskbar-styler.wh.cpp
    // See NOTICE.md.

    struct PropertyFilter
    {
        std::wstring property;
        std::wstring value;
    };

    struct ElementMatcher
    {
        bool anyAncestors = false;        // the `*` entry; every other member is unused then
        std::wstring typeName;            // runtime class name, empty = any type
        std::wstring name;                // x:Name, empty = any name
        std::wstring visualStateGroup;    // empty = no requirement
        int index = 0;                    // 1-based child index, 0 = any
        std::vector<PropertyFilter> filters;
    };

    struct Selector
    {
        std::vector<ElementMatcher> path;   // left (ancestor) to right (the target element)
    };

    // The runtime class name a type written in a selector stands for.
    std::wstring AdjustTypeName(std::wstring_view type);

    // Parses `text`. On failure returns false and says what is wrong and where
    // in `error`.
    bool ParseSelector(std::wstring_view text, Selector& selector, std::wstring& error);

    // The canonical spelling of a selector: what ParseSelector read, with the
    // type names spelt out and one space around each `>`.
    std::wstring DescribeSelector(const Selector& selector);
}
