#pragma once

#include <string>
#include <vector>

namespace deckle::rules
{
    // The settings document Deckle sends in an "apply" command, as the engine
    // reads it. The C# side builds it (TaskbarDocumentBuilder); the DLL knows no
    // setting by name, only rules and the named behaviors below.
    //
    //   { "rules": [ { "id": "...", "target": "Type#Name > ...", "property": "Fill",
    //                  "value": "Transparent", "visualState": "VerticalOrientation" } ],
    //     "behaviors": { "clock": { "enabled": true, "locale": "en-US",
    //                               "upper": [ { "time": "HH" } ], "lower": [ { "date": "dd" } ] } } }
    //
    // A rule's value is the text a XAML Setter would hold: an attribute value
    // such as Transparent, 24 or #C8C8C8, or, when it starts with '<', element
    // content such as <SolidColorBrush Color="Red"/>.

    struct RuleDefinition
    {
        std::wstring id;
        std::wstring target;
        std::wstring property;
        std::wstring value;
        std::wstring visualState;   // empty = always; else the state of the target's @Group that enables the rule
    };

    // One line of the clock's text: a date or a time picture, as
    // GetDateFormatEx or GetTimeFormatEx reads it ("MMM.", "dd", "ddd", "HH", "mm").
    struct ClockLine
    {
        bool isDate = false;
        std::wstring format;
    };

    // The clock behavior (behaviors/clock.cpp). The text the clock shows is
    // two TextBlocks, one above the other; the DLL writes the lines itself.
    struct ClockConfiguration
    {
        bool enabled = false;
        std::wstring locale = L"en-US";   // never the user's locale: the wording is the document's
        std::vector<ClockLine> upper;     // the lines of the first TextBlock (TimeInnerTextBlock)
        std::vector<ClockLine> lower;     // the lines of the second TextBlock (DateInnerTextBlock)
    };

    // The Start menu height behavior (behaviors/start_menu_height.cpp): the
    // menu's FrameRoot is as high as the Start window, whatever the scale.
    //   "startMenuHeight": { "enabled": true }
    // It only means something in StartMenuExperienceHost; in explorer no
    // element matches and the behavior reports never_matched.
    struct StartMenuHeightConfiguration
    {
        bool enabled = false;
    };

    // The Start menu width behavior (behaviors/start_menu_width.cpp): the menu's
    // panel is as wide as asked, or as the Start window allows, and never wider.
    //   "startMenuWidth": { "enabled": true, "fill": false, "width": 640 }
    // width is in effective pixels and ignored when fill is true. Like the
    // height, it only matches in StartMenuExperienceHost.
    struct StartMenuWidthConfiguration
    {
        bool enabled = false;
        bool fill = false;
        double width = 0;
    };

    struct SettingsDocument
    {
        std::vector<RuleDefinition> rules;
        ClockConfiguration clock;
        StartMenuHeightConfiguration startMenuHeight;
        StartMenuWidthConfiguration startMenuWidth;
        std::vector<std::wstring> problems;   // what could not be read; the rest still applies
    };

    SettingsDocument ParseSettingsDocument(winrt::Windows::Data::Json::JsonObject const& document);
}
