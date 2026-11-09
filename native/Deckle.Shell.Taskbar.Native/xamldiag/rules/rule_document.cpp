#include "pch.h"
#include "rule_document.h"

namespace
{
    namespace json = winrt::Windows::Data::Json;

    std::wstring ReadString(json::JsonObject const& object, std::wstring_view key)
    {
        if (!object.HasKey(key)) return {};
        auto value = object.GetNamedValue(key);
        return value.ValueType() == json::JsonValueType::String ? std::wstring(value.GetString()) : std::wstring();
    }

    // The clock's lines: [ { "time": "HH" }, { "date": "dd" } ].
    void ReadLines(json::JsonObject const& clock, std::wstring_view key, std::vector<deckle::rules::ClockLine>& lines,
                   std::vector<std::wstring>& problems)
    {
        if (!clock.HasKey(key) || clock.GetNamedValue(key).ValueType() != json::JsonValueType::Array) return;

        for (auto const& item : clock.GetNamedArray(key))
        {
            if (item.ValueType() != json::JsonValueType::Object)
            {
                problems.push_back(L"clock." + std::wstring(key) + L": a line is not an object");
                continue;
            }
            auto line = item.GetObject();
            std::wstring date = ReadString(line, L"date");
            std::wstring time = ReadString(line, L"time");
            if (date.empty() == time.empty())
            {
                problems.push_back(L"clock." + std::wstring(key) + L": a line has either a date or a time picture");
                continue;
            }
            lines.push_back({ !date.empty(), date.empty() ? time : date });
        }
    }
}

namespace deckle::rules
{
    SettingsDocument ParseSettingsDocument(json::JsonObject const& document)
    {
        SettingsDocument result;

        if (document.HasKey(L"rules") && document.GetNamedValue(L"rules").ValueType() == json::JsonValueType::Array)
        {
            int position = 0;
            for (auto const& item : document.GetNamedArray(L"rules"))
            {
                ++position;
                if (item.ValueType() != json::JsonValueType::Object)
                {
                    result.problems.push_back(L"rule " + std::to_wstring(position) + L" is not an object");
                    continue;
                }

                auto object = item.GetObject();
                RuleDefinition rule;
                rule.id = ReadString(object, L"id");
                rule.target = ReadString(object, L"target");
                rule.property = ReadString(object, L"property");
                rule.value = ReadString(object, L"value");
                rule.visualState = ReadString(object, L"visualState");
                if (rule.id.empty()) rule.id = L"rule" + std::to_wstring(position);

                // A rule that cannot be read still gets a status line (state
                // error), so the missing part is named once, by the engine.
                result.rules.push_back(std::move(rule));
            }
        }

        if (document.HasKey(L"behaviors") && document.GetNamedValue(L"behaviors").ValueType() == json::JsonValueType::Object)
        {
            auto behaviors = document.GetNamedObject(L"behaviors");
            if (behaviors.HasKey(L"clock") && behaviors.GetNamedValue(L"clock").ValueType() == json::JsonValueType::Object)
            {
                auto clock = behaviors.GetNamedObject(L"clock");
                result.clock.enabled = clock.HasKey(L"enabled") && clock.GetNamedValue(L"enabled").ValueType() == json::JsonValueType::Boolean
                                       && clock.GetNamedBoolean(L"enabled");
                if (std::wstring locale = ReadString(clock, L"locale"); !locale.empty()) result.clock.locale = locale;
                ReadLines(clock, L"upper", result.clock.upper, result.problems);
                ReadLines(clock, L"lower", result.clock.lower, result.problems);
            }
            if (behaviors.HasKey(L"startMenuHeight") && behaviors.GetNamedValue(L"startMenuHeight").ValueType() == json::JsonValueType::Object)
            {
                auto height = behaviors.GetNamedObject(L"startMenuHeight");
                result.startMenuHeight.enabled = height.HasKey(L"enabled") && height.GetNamedValue(L"enabled").ValueType() == json::JsonValueType::Boolean
                                                 && height.GetNamedBoolean(L"enabled");
            }
        }

        return result;
    }
}
