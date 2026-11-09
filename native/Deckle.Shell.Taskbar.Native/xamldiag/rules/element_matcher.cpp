#include "pch.h"
#include "element_matcher.h"

// Adapted from Windhawk's windows-11-taskbar-styler mod, GPL-3.0. See
// element_matcher.h and NOTICE.md.

namespace
{
    namespace wux = winrt::Windows::UI::Xaml;
    namespace wuxm = winrt::Windows::UI::Xaml::Media;
    using deckle::rules::ElementMatcher;

    wux::DependencyObject ParentOf(wux::DependencyObject const& element)
    {
        try { return wuxm::VisualTreeHelper::GetParent(element); }
        catch (...) { return nullptr; }
    }

    // The filters a selector can hold. A property not listed matches nothing,
    // so a rule with a filter this build cannot read never applies, and its
    // status says so.
    bool MatchesFilter(wux::DependencyObject const& element, deckle::rules::PropertyFilter const& filter)
    {
        if (filter.property == L"Name")
        {
            auto framework = element.try_as<wux::FrameworkElement>();
            return framework && framework.Name() == filter.value;
        }
        if (filter.property == L"AutomationProperties.AutomationId")
            return wux::Automation::AutomationProperties::GetAutomationId(element) == filter.value;
        if (filter.property == L"AutomationProperties.Name")
            return wux::Automation::AutomationProperties::GetName(element) == filter.value;
        return false;
    }

    bool MatchesOne(wux::DependencyObject const& element, ElementMatcher const& matcher)
    {
        if (!matcher.typeName.empty() && winrt::get_class_name(element) != matcher.typeName) return false;

        if (!matcher.name.empty())
        {
            auto framework = element.try_as<wux::FrameworkElement>();
            if (!framework || framework.Name() != matcher.name) return false;
        }

        if (matcher.index > 0)
        {
            auto parent = ParentOf(element);
            if (!parent) return false;
            try
            {
                if (wuxm::VisualTreeHelper::GetChildrenCount(parent) < matcher.index) return false;
                auto child = wuxm::VisualTreeHelper::GetChild(parent, matcher.index - 1);
                if (child != element) return false;
            }
            catch (...) { return false; }
        }

        for (auto const& filter : matcher.filters)
        {
            if (!MatchesFilter(element, filter)) return false;
        }

        if (!matcher.visualStateGroup.empty())
        {
            auto framework = element.try_as<wux::FrameworkElement>();
            if (!framework || !deckle::rules::FindVisualStateGroup(framework, matcher.visualStateGroup)) return false;
        }

        return true;
    }

    // `element` already matches path[index]; do its ancestors match what comes before?
    bool MatchesAncestors(wux::DependencyObject const& element, std::vector<ElementMatcher> const& path, size_t index)
    {
        if (index == 0) return true;   // the first matcher may sit anywhere

        const ElementMatcher& before = path[index - 1];
        if (!before.anyAncestors)
        {
            auto parent = ParentOf(element);
            return parent && MatchesOne(parent, before) && MatchesAncestors(parent, path, index - 1);
        }

        // `*` before this element: any chain of ancestors, none included, then
        // the matcher before the star (a star cannot lead a path in practice,
        // but one that does matches everything).
        if (index == 1) return true;
        const ElementMatcher& beforeStar = path[index - 2];
        for (auto ancestor = ParentOf(element); ancestor; ancestor = ParentOf(ancestor))
        {
            if (MatchesOne(ancestor, beforeStar) && MatchesAncestors(ancestor, path, index - 2)) return true;
        }
        return false;
    }
}

namespace deckle::rules
{
    wux::VisualStateGroup FindVisualStateGroup(wux::FrameworkElement const& element, std::wstring_view name)
    {
        try
        {
            auto groups = wux::VisualStateManager::GetVisualStateGroups(element);
            if (!groups) return nullptr;
            for (auto const& group : groups)
            {
                if (group.Name() == name) return group;
            }
        }
        catch (...) {}
        return nullptr;
    }

    bool MatchesSelector(wux::FrameworkElement const& element, const Selector& selector)
    {
        if (selector.path.empty()) return false;
        const size_t last = selector.path.size() - 1;
        return MatchesOne(element, selector.path[last]) && MatchesAncestors(element, selector.path, last);
    }
}
