#include "pch.h"
#include "clock.h"

// The clock lines mirror what Windhawk's taskbar-clock-customization mod shows
// (its TopLine and BottomLine, formatted with GetTimeFormatEx and GetDateFormatEx),
// GPL-3.0, https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-clock-customization.wh.cpp
// The mod gets its text in by hooking those two functions inside
// ClockSystemTrayIconDataModel::RefreshIcon, which needs the PDB symbols.
// This behavior writes the TextBlocks' Text property instead, from the XAML
// side, and keeps it there with the engine's property override (a callback
// that sets the text back when Explorer writes it, and a refresh each minute).
// See NOTICE.md.
//
// The probe (T8 step 1) is the verification in the report: set the text once
// and see whether it survives the next minute and whether the property
// callback fires. The override's counters (reasserted) and the clock_text
// records are that evidence.

namespace
{
    namespace wux = winrt::Windows::UI::Xaml;
    namespace wuxm = winrt::Windows::UI::Xaml::Media;

    wux::DependencyObject ParentOf(wux::DependencyObject const& element)
    {
        try { return wuxm::VisualTreeHelper::GetParent(element); }
        catch (...) { return nullptr; }
    }

    std::wstring FormatLine(const deckle::rules::ClockLine& line, const wchar_t* locale, const SYSTEMTIME& now)
    {
        wchar_t buffer[128];
        int length = line.isDate
            ? GetDateFormatEx(locale, 0, &now, line.format.c_str(), buffer, static_cast<int>(std::size(buffer)), nullptr)
            : GetTimeFormatEx(locale, 0, &now, line.format.c_str(), buffer, static_cast<int>(std::size(buffer)));
        // The count includes the terminating null.
        return length > 0 ? std::wstring(buffer, static_cast<size_t>(length - 1)) : std::wstring();
    }
}

namespace deckle::behaviors
{
    ClockBlock ClassifyClockBlock(wux::FrameworkElement const& element)
    {
        auto text = element.try_as<winrt::Windows::UI::Xaml::Controls::TextBlock>();
        if (!text) return ClockBlock::None;

        ClockBlock block = ClockBlock::None;
        if (text.Name() == L"TimeInnerTextBlock") block = ClockBlock::Upper;
        else if (text.Name() == L"DateInnerTextBlock") block = ClockBlock::Lower;
        else return ClockBlock::None;

        // TextBlock < StackPanel < Grid#ContainerGrid < SystemTray.DateTimeIconContent
        auto stack = ParentOf(element);
        auto grid = stack ? ParentOf(stack) : nullptr;
        auto content = grid ? ParentOf(grid) : nullptr;
        if (!content || winrt::get_class_name(content) != L"SystemTray.DateTimeIconContent") return ClockBlock::None;
        return block;
    }

    std::wstring FormatClockBlock(const rules::ClockConfiguration& configuration, ClockBlock block)
    {
        SYSTEMTIME now;
        GetLocalTime(&now);

        const auto& lines = block == ClockBlock::Upper ? configuration.upper : configuration.lower;
        std::wstring text;
        for (const auto& line : lines)
        {
            if (!text.empty()) text += L'\n';
            text += FormatLine(line, configuration.locale.c_str(), now);
        }
        return text;
    }

    int64_t CurrentMinute()
    {
        SYSTEMTIME now;
        GetLocalTime(&now);
        return ((static_cast<int64_t>(now.wYear) * 12 + now.wMonth) * 31 + now.wDay) * 1440 + now.wHour * 60 + now.wMinute;
    }
}
