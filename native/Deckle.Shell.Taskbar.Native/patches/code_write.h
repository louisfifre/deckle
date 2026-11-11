#pragma once

#include <initializer_list>

namespace deckle::patches
{
    // A span of addresses [begin, end) no other thread may be stopped in while
    // code changes: the trampoline of a detour, or this DLL's own image.
    struct CodeRange
    {
        uintptr_t begin = 0;
        uintptr_t end = 0;
    };

    // Writes `length` bytes over live code of explorer. Every other thread of
    // the process is held still for the write, and the write waits for a moment
    // when none of them is stopped past the first byte that changes (it would
    // resume in the middle of an instruction that is no longer there) nor inside
    // one of `busy`. A thread about to run the first byte is fine: it will run
    // the new instruction whole. False when no such moment came in about 50 ms,
    // or when the page could not be made writable.
    bool WriteCode(uint8_t* address, uint8_t const* bytes, size_t length, std::initializer_list<CodeRange> busy = {});

    // The span this DLL occupies in memory.
    CodeRange OwnImage();
}
