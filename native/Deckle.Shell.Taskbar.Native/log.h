#pragma once

namespace deckle
{
    enum class LogLevel
    {
        Verbose,
        Info,
        Warning,
        Error,
    };

    struct LogField
    {
        std::wstring key;
        std::wstring value;
    };

    // Writes one log record. The record never touches a file: it joins the
    // channel's queue, and the channel's own thread sends it to Deckle over the
    // pipe, where DeckleShellTaskbarSource re-emits it. Callable from any
    // thread, explorer's UI threads included, and it never blocks on the pipe.
    //
    // `event` is a stable snake_case name, `message` a sentence for a person,
    // `fields` the values, all as text. Counts, handles and type names only:
    // nothing a person typed or keeps.
    void Log(LogLevel level, std::wstring_view event, std::wstring_view message,
             std::initializer_list<LogField> fields = {});
}
