#include "pch.h"
#include "log.h"
#include "channel.h"
#include "util.h"

namespace
{
    using namespace winrt::Windows::Data::Json;

    const wchar_t* LevelName(deckle::LogLevel level)
    {
        switch (level)
        {
        case deckle::LogLevel::Verbose: return L"verbose";
        case deckle::LogLevel::Info:    return L"info";
        case deckle::LogLevel::Warning: return L"warning";
        default:                        return L"error";
        }
    }
}

namespace deckle
{
    void Log(LogLevel level, std::wstring_view event, std::wstring_view message,
             std::initializer_list<LogField> fields)
    {
        try
        {
            JsonObject record;
            record.SetNamedValue(L"t", JsonValue::CreateStringValue(L"log"));
            record.SetNamedValue(L"level", JsonValue::CreateStringValue(LevelName(level)));
            record.SetNamedValue(L"event", JsonValue::CreateStringValue(winrt::hstring(event)));
            record.SetNamedValue(L"msg", JsonValue::CreateStringValue(winrt::hstring(message)));

            JsonObject values;
            for (const LogField& field : fields)
            {
                values.SetNamedValue(winrt::hstring(field.key), JsonValue::CreateStringValue(winrt::hstring(field.value)));
            }
            record.SetNamedValue(L"fields", values);

            // Stringify writes a single line: control characters are escaped.
            Channel::Instance().Post(util::ToUtf8(record.Stringify()));
        }
        catch (...)
        {
            // A log line must never take explorer down.
        }
    }
}
