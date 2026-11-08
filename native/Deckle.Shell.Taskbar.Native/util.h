#pragma once

namespace deckle::util
{
    // UTF-8 <-> UTF-16, the encoding of the pipe (JSON lines) and of Windows.
    std::string ToUtf8(std::wstring_view text);
    std::wstring FromUtf8(std::string_view text);

    // \\.\pipe\Deckle.Taskbar.<terminal session id>: the pipe Deckle's side
    // serves. The session id keeps two signed-in users on one machine apart.
    std::wstring PipeName();

    // Full path of the DLL file this code runs from.
    std::wstring ModulePath(HMODULE module);

    // 0x%016llX, the spelling of an InstanceHandle in a log record.
    std::wstring HandleText(uint64_t value);
}
