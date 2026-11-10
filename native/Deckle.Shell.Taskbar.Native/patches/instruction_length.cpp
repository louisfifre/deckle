#include "pch.h"
#include "instruction_length.h"

// The opcode tables follow the Intel 64 and IA-32 Architectures Software
// Developer's Manual, volume 2, appendix A (opcode map), in 64-bit mode. Only
// the length matters here: which bytes follow the opcode (a ModRM, a SIB, a
// displacement, an immediate), never what the instruction does.

namespace
{
    using deckle::patches::Instruction;

    constexpr size_t MaxLength = 15;   // the architecture's own limit

    bool IsLegacyPrefix(uint8_t b)
    {
        switch (b)
        {
        case 0x66: case 0x67: case 0xF0: case 0xF2: case 0xF3:
        case 0x2E: case 0x36: case 0x3E: case 0x26: case 0x64: case 0x65:
            return true;
        default:
            return false;
        }
    }

    // What follows an opcode: a ModRM byte, and an immediate of `immediate` bytes.
    struct Shape
    {
        bool known = false;
        bool modrm = false;
        uint8_t immediate = 0;
    };

    constexpr Shape None{ true, false, 0 };
    constexpr Shape ModRM{ true, true, 0 };
    Shape Imm(uint8_t size) { return { true, false, size }; }
    Shape ModRMImm(uint8_t size) { return { true, true, size }; }

    // The one-byte map. `z` is the size of a word-or-doubleword immediate (2
    // under the 66 prefix, else 4); `v` the size of mov reg, imm (8 with REX.W).
    Shape OneByte(uint8_t op, uint8_t z, uint8_t v)
    {
        if (op < 0x40)
        {
            // The eight ALU groups (add, or, adc, sbb, and, sub, xor, cmp), each
            // laid out the same: four ModRM forms, then al, imm8 and eax, imm32.
            // The column's last two (push/pop of a segment, daa...) do not exist
            // in 64-bit mode; 0F, 26, 2E, 36, 3E never reach here.
            switch (op & 7)
            {
            case 0: case 1: case 2: case 3: return ModRM;
            case 4: return Imm(1);
            case 5: return Imm(z);
            default: return {};
            }
        }
        if (op >= 0x50 && op <= 0x5F) return None;                // push, pop reg
        if (op == 0x63) return ModRM;                             // movsxd
        if (op == 0x68) return Imm(z);                            // push imm
        if (op == 0x69) return ModRMImm(z);                       // imul r, r/m, imm
        if (op == 0x6A) return Imm(1);
        if (op == 0x6B) return ModRMImm(1);
        if (op >= 0x6C && op <= 0x6F) return None;                // ins, outs
        if (op == 0x80 || op == 0x83) return ModRMImm(1);         // group 1, imm8
        if (op == 0x81) return ModRMImm(z);                       // group 1, imm
        if (op >= 0x84 && op <= 0x8F) return ModRM;               // test, xchg, mov, lea, pop r/m
        if ((op >= 0x90 && op <= 0x99) || (op >= 0x9B && op <= 0x9F)) return None;
        if (op == 0xA8) return Imm(1);                            // test al, imm8
        if (op == 0xA9) return Imm(z);
        if ((op >= 0xA4 && op <= 0xA7) || (op >= 0xAA && op <= 0xAF)) return None;   // string ops
        if (op >= 0xB0 && op <= 0xB7) return Imm(1);              // mov r8, imm8
        if (op >= 0xB8 && op <= 0xBF) return Imm(v);              // mov reg, imm
        if (op == 0xC0 || op == 0xC1 || op == 0xC6) return ModRMImm(1);
        if (op == 0xC7) return ModRMImm(z);
        if (op == 0xC8) return Imm(3);                            // enter
        if (op == 0xC9) return None;                              // leave
        if (op == 0xCD) return Imm(1);                            // int imm8
        if (op >= 0xD0 && op <= 0xD3) return ModRM;               // shifts
        if (op == 0xD7) return None;
        if (op >= 0xD8 && op <= 0xDF) return ModRM;               // x87
        if (op >= 0xE4 && op <= 0xE7) return Imm(1);              // in, out imm8
        if (op >= 0xEC && op <= 0xEF) return None;
        if (op == 0xF1 || op == 0xF4 || op == 0xF5 || (op >= 0xF8 && op <= 0xFD)) return None;
        if (op == 0xF6 || op == 0xF7 || op == 0xFE || op == 0xFF) return ModRM;   // F6/F7's test adds an immediate below
        // Refused: relative branches (70-7F, E0-E3, E8, E9, EB), returns (C2, C3,
        // CA, CB, CF), int3 (CC, padding past the function's end), moffs moves
        // (A0-A3), VEX and EVEX (C4, C5, 62), and what 64-bit mode lacks.
        return {};
    }

    // The 0F map.
    Shape TwoByte(uint8_t op)
    {
        if (op == 0x0F || (op >= 0x80 && op <= 0x8F)) return {};  // 3DNow!, jcc rel32
        switch (op)
        {
        case 0x05: case 0x06: case 0x07: case 0x08: case 0x09: case 0x0B:
        case 0x30: case 0x31: case 0x32: case 0x33: case 0x34: case 0x35: case 0x37:
        case 0x77: case 0xA0: case 0xA1: case 0xA2: case 0xA8: case 0xA9: case 0xAA:
            return None;   // syscall, ud2, rdtsc, cpuid, push/pop fs/gs...
        default:
            break;
        }
        if (op >= 0xC8 && op <= 0xCF) return None;                // bswap
        if ((op >= 0x70 && op <= 0x73) || op == 0xA4 || op == 0xAC || op == 0xBA || op == 0xC2 || (op >= 0xC4 && op <= 0xC6))
            return ModRMImm(1);                                   // pshufd, shld/shrd imm, bt imm, cmpps...
        return ModRM;
    }
}

namespace deckle::patches
{
    Instruction DecodeInstruction(uint8_t const* code, size_t available)
    {
        const size_t limit = (std::min)(available, MaxLength);
        size_t i = 0;
        auto next = [&](uint8_t& b) { if (i >= limit) return false; b = code[i++]; return true; };

        bool operand16 = false, address32 = false, rexW = false;
        uint8_t b = 0;
        if (!next(b)) return {};
        while (IsLegacyPrefix(b))
        {
            operand16 |= b == 0x66;
            address32 |= b == 0x67;
            if (!next(b)) return {};
        }
        if ((b & 0xF0) == 0x40)   // REX, the last prefix
        {
            rexW = (b & 0x08) != 0;
            if (!next(b)) return {};
        }

        const uint8_t z = operand16 ? 2 : 4;
        const uint8_t v = rexW ? 8 : z;
        bool oneByte = b != 0x0F;
        uint8_t op = b;
        Shape shape;
        if (oneByte)
        {
            shape = OneByte(op, z, v);
        }
        else
        {
            if (!next(op)) return {};
            if (op == 0x38 || op == 0x3A)
            {
                uint8_t third = 0;
                if (!next(third)) return {};
                shape = op == 0x38 ? ModRM : ModRMImm(1);
            }
            else
            {
                shape = TwoByte(op);
            }
        }
        if (!shape.known) return {};

        Instruction result;
        size_t immediate = shape.immediate;
        if (shape.modrm)
        {
            uint8_t modrm = 0;
            if (!next(modrm)) return {};
            const uint8_t mod = modrm >> 6, reg = (modrm >> 3) & 7, rm = modrm & 7;
            if (oneByte && reg <= 1 && op == 0xF6) immediate = 1;   // test r/m8, imm8
            if (oneByte && reg <= 1 && op == 0xF7) immediate = z;   // test r/m, imm

            if (mod != 3)
            {
                size_t displacement = mod == 1 ? 1 : mod == 2 ? 4 : 0;
                if (rm == 4)
                {
                    uint8_t sib = 0;
                    if (!next(sib)) return {};
                    if (mod == 0 && (sib & 7) == 5) displacement = 4;   // no base: disp32
                }
                else if (mod == 0 && rm == 5)
                {
                    // RIP-relative; under 67 it would be EIP-relative, which no
                    // code of explorer uses.
                    if (address32) return {};
                    result.ripDisplacement = static_cast<int8_t>(i);
                    displacement = 4;
                }
                i += displacement;
            }
        }
        i += immediate;
        if (i > limit) return {};
        result.length = static_cast<uint8_t>(i);
        return result;
    }
}
