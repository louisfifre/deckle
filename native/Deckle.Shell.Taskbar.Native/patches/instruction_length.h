#pragma once

namespace deckle::patches
{
    // One x64 instruction as a detour needs to know it: how long it is, and
    // where its RIP-relative displacement lies when it has one.
    //
    // A detour moves the first instructions of a function into a trampoline.
    // An instruction moves as it is unless it addresses memory relative to its
    // own position (mov xmm, [rip+disp32]): moved elsewhere it would read
    // another address, so its 32-bit displacement is corrected by the distance
    // it moved. A relative branch (jcc, jmp, call rel) or a return would need
    // more than a correction, and the functions Deckle detours do not start with
    // one: those are refused, as is any instruction this decoder does not know.
    struct Instruction
    {
        uint8_t length = 0;          // 0: unknown, or not movable
        int8_t ripDisplacement = -1; // offset of the disp32 in the instruction, -1 when none
    };

    // Decodes the instruction at `code`, reading no more than `available` bytes.
    // Covers the legacy prefixes, REX, the one-byte map and the 0F, 0F 38 and
    // 0F 3A maps (the general instructions and SSE); VEX and EVEX (AVX) are
    // refused.
    Instruction DecodeInstruction(uint8_t const* code, size_t available);
}
