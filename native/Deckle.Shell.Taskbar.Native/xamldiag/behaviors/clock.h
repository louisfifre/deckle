#pragma once

#include "../rules/rule_document.h"

namespace deckle::behaviors
{
    // The clock behavior: the taskbar clock shows two TextBlocks, one above the
    // other (TimeInnerTextBlock, then DateInnerTextBlock), and Deckle writes
    // their text itself, in the lines the document names, in the document's
    // locale. Nothing here is a rule because the text is not a value in a
    // document: it changes every minute and is formatted by Windows.
    //
    // Why writing the text works at all (and the probe that says so) is in
    // clock.cpp.
    enum class ClockBlock
    {
        None,
        Upper,   // TimeInnerTextBlock
        Lower,   // DateInnerTextBlock
    };

    // Which clock TextBlock `element` is, if any: a TextBlock named
    // TimeInnerTextBlock or DateInnerTextBlock inside SystemTray.DateTimeIconContent.
    ClockBlock ClassifyClockBlock(winrt::Windows::UI::Xaml::FrameworkElement const& element);

    // The text of one block now: its lines, formatted for the local time, joined by line breaks.
    std::wstring FormatClockBlock(const rules::ClockConfiguration& configuration, ClockBlock block);

    // Minutes since some fixed instant, in local time: changes when the clock's text can.
    int64_t CurrentMinute();
}
