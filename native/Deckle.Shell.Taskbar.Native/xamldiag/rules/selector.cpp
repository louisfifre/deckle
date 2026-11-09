#include "pch.h"
#include "selector.h"
#include <climits>

// Adapted from Windhawk's windows-11-taskbar-styler mod, GPL-3.0. See selector.h
// and NOTICE.md.

namespace
{
    bool IsNameChar(wchar_t c)
    {
        return (c >= L'0' && c <= L'9') || (c >= L'A' && c <= L'Z') || (c >= L'a' && c <= L'z') || c == L'_';
    }

    bool IsTypeChar(wchar_t c)
    {
        return IsNameChar(c) || c == L'.' || c == L':';
    }

    bool IsSpace(wchar_t c)
    {
        return c == L' ' || c == L'\t' || c == L'\r' || c == L'\n';
    }

    bool IsOneOf(std::wstring_view value, std::initializer_list<std::wstring_view> list)
    {
        return std::find(list.begin(), list.end(), value) != list.end();
    }

    bool AllDigits(std::wstring_view text)
    {
        return !text.empty() && std::all_of(text.begin(), text.end(), [](wchar_t c) { return c >= L'0' && c <= L'9'; });
    }
}

namespace deckle::rules
{
    std::wstring AdjustTypeName(std::wstring_view type)
    {
        // The two prefixes the styler gives to the namespaces of Explorer's own
        // XAML assemblies.
        if (type.rfind(L"taskbar:", 0) == 0) return L"Taskbar." + std::wstring(type.substr(8));
        if (type.rfind(L"systemtray:", 0) == 0) return L"SystemTray." + std::wstring(type.substr(11));

        if (type.find(L'.') != std::wstring_view::npos) return std::wstring(type);

        if (IsOneOf(type, { L"Rectangle", L"Ellipse", L"Line", L"Path", L"Polygon", L"Polyline" }))
            return L"Windows.UI.Xaml.Shapes." + std::wstring(type);
        if (IsOneOf(type, { L"ToggleButton", L"RepeatButton", L"ButtonBase", L"Thumb", L"ScrollBar", L"Selector" }))
            return L"Windows.UI.Xaml.Controls.Primitives." + std::wstring(type);
        if (IsOneOf(type, { L"FrameworkElement", L"UIElement" }))
            return L"Windows.UI.Xaml." + std::wstring(type);

        return L"Windows.UI.Xaml.Controls." + std::wstring(type);
    }

    bool ParseSelector(std::wstring_view text, Selector& selector, std::wstring& error)
    {
        selector.path.clear();
        error.clear();

        size_t pos = 0;
        const size_t size = text.size();
        auto fail = [&](std::wstring_view message)
        {
            error = std::wstring(message) + L" (at position " + std::to_wstring(pos) + L")";
            selector.path.clear();
            return false;
        };
        auto skipSpaces = [&] { while (pos < size && IsSpace(text[pos])) ++pos; };

        skipSpaces();
        if (pos == size) return fail(L"the selector is empty");

        for (;;)
        {
            skipSpaces();
            ElementMatcher matcher;

            if (pos < size && text[pos] == L'*')
            {
                matcher.anyAncestors = true;
                ++pos;
            }
            else
            {
                size_t start = pos;
                while (pos < size && IsTypeChar(text[pos])) ++pos;
                std::wstring type(text.substr(start, pos - start));

                if (pos < size && text[pos] == L'#')
                {
                    start = ++pos;
                    while (pos < size && IsNameChar(text[pos])) ++pos;
                    matcher.name = std::wstring(text.substr(start, pos - start));
                    if (matcher.name.empty()) return fail(L"'#' needs an element name");
                }

                if (pos < size && text[pos] == L'@')
                {
                    start = ++pos;
                    while (pos < size && IsNameChar(text[pos])) ++pos;
                    matcher.visualStateGroup = std::wstring(text.substr(start, pos - start));
                    if (matcher.visualStateGroup.empty()) return fail(L"'@' needs a visual state group name");
                }

                while (pos < size && text[pos] == L'[')
                {
                    size_t close = text.find(L']', pos);
                    if (close == std::wstring_view::npos) return fail(L"'[' has no closing ']'");
                    std::wstring_view inside = text.substr(pos + 1, close - pos - 1);

                    if (AllDigits(inside))
                    {
                        if (inside.size() > 6 || std::stoi(std::wstring(inside)) < 1) return fail(L"a child index is a number from 1");
                        matcher.index = std::stoi(std::wstring(inside));
                    }
                    else if (size_t eq = inside.find(L'='); eq != std::wstring_view::npos && eq > 0)
                    {
                        matcher.filters.push_back({ std::wstring(inside.substr(0, eq)), std::wstring(inside.substr(eq + 1)) });
                    }
                    else
                    {
                        return fail(L"'[...]' holds a child index or Property=Value");
                    }
                    pos = close + 1;
                }

                if (!type.empty()) matcher.typeName = AdjustTypeName(type);

                if (matcher.typeName.empty() && matcher.name.empty() && matcher.visualStateGroup.empty()
                    && matcher.index == 0 && matcher.filters.empty())
                {
                    return fail(L"an element needs a type, a name, a state group or a filter");
                }
            }

            selector.path.push_back(std::move(matcher));

            skipSpaces();
            if (pos == size) break;
            if (text[pos] != L'>') return fail(L"expected '>' between two elements");
            ++pos;
            skipSpaces();
            if (pos == size) return fail(L"the selector ends with '>'");
        }

        if (selector.path.back().anyAncestors) return fail(L"a selector cannot end with '*'");
        return true;
    }

    std::wstring DescribeSelector(const Selector& selector)
    {
        std::wstring text;
        for (const ElementMatcher& matcher : selector.path)
        {
            if (!text.empty()) text += L" > ";
            if (matcher.anyAncestors)
            {
                text += L'*';
                continue;
            }
            text += matcher.typeName;
            if (!matcher.name.empty()) text += L"#" + matcher.name;
            if (!matcher.visualStateGroup.empty()) text += L"@" + matcher.visualStateGroup;
            if (matcher.index > 0) text += L"[" + std::to_wstring(matcher.index) + L"]";
            for (const PropertyFilter& filter : matcher.filters) text += L"[" + filter.property + L"=" + filter.value + L"]";
        }
        return text;
    }
}

// The parser's test hook: C# tests load the DLL and call this, so the tests
// run the code explorer runs and no second parser exists to drift. Writes the
// canonical form of `text` into `output` and returns its length, or writes
// the error and returns -1 minus its length. Returns INT_MIN when the buffer
// is too small.
extern "C" int __stdcall DeckleTaskbarDescribeSelector(const wchar_t* text, wchar_t* output, int capacity)
{
    deckle::rules::Selector selector;
    std::wstring error;
    std::wstring result;
    bool ok = deckle::rules::ParseSelector(text ? text : L"", selector, error);
    result = ok ? deckle::rules::DescribeSelector(selector) : error;

    if (static_cast<int>(result.size()) + 1 > capacity) return INT_MIN;
    wcscpy_s(output, static_cast<size_t>(capacity), result.c_str());
    return ok ? static_cast<int>(result.size()) : -1 - static_cast<int>(result.size());
}
