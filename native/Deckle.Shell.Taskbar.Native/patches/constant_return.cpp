#include "pch.h"
#include "constant_return.h"
#include "code_write.h"
#include "../log.h"

#include <cstring>

namespace
{
    namespace json = winrt::Windows::Data::Json;
    using deckle::patches::ConstantReturn;
    using deckle::patches::FunctionSite;

    constexpr size_t PatchLength = FunctionSite::PrologueLength;
    using Bytes = std::array<uint8_t, PatchLength>;

    // mov rax, imm64 (48 B8 + 8 bytes) ; movq xmm0, rax (66 48 0F 6E C0) ; ret (C3).
    // A double comes back in xmm0 on x64.
    Bytes PatchFor(double value)
    {
        Bytes bytes{ 0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0, 0x66, 0x48, 0x0F, 0x6E, 0xC0, 0xC3 };
        std::memcpy(bytes.data() + 2, &value, sizeof value);
        return bytes;
    }

    bool IsOurPatch(Bytes const& bytes)
    {
        static constexpr uint8_t tail[] = { 0x66, 0x48, 0x0F, 0x6E, 0xC0, 0xC3 };
        return bytes[0] == 0x48 && bytes[1] == 0xB8 && std::memcmp(bytes.data() + 10, tail, sizeof tail) == 0;
    }

    struct Patched
    {
        std::wstring id;
        uint8_t* address = nullptr;
        Bytes original{};
    };

    std::mutex g_lock;
    std::vector<Patched> g_patched;   // what is in force, under g_lock

    bool Write(uint8_t* address, Bytes const& bytes)
    {
        return deckle::patches::WriteCode(address, bytes.data(), bytes.size());
    }

    std::wstring Number(double value)
    {
        wchar_t text[32];
        swprintf_s(text, L"%g", value);
        return text;
    }

    void Report(deckle::LogLevel level, std::wstring_view event, std::wstring_view message, ConstantReturn const& entry,
             std::wstring_view reason = {})
    {
        deckle::Log(level, event, message,
                    { { L"id", entry.site.id }, { L"module", entry.site.module }, { L"value", Number(entry.value) },
                      { L"reason", std::wstring(reason) } });
    }
}

namespace deckle::patches
{
    std::vector<ConstantReturn> Parse(json::JsonObject const& document, std::vector<std::wstring>& problems)
    {
        std::vector<ConstantReturn> set;
        if (!document.HasKey(L"constantReturns") || document.GetNamedValue(L"constantReturns").ValueType() != json::JsonValueType::Array)
            return set;

        for (auto const& item : document.GetNamedArray(L"constantReturns"))
        {
            if (item.ValueType() != json::JsonValueType::Object) { problems.push_back(L"constantReturns: an entry is not an object"); continue; }
            auto object = item.GetObject();
            ConstantReturn entry;
            if (!ReadSite(object, entry.site) || !ReadNumber(object, L"value", entry.value))
            {
                problems.push_back(L"constantReturns: an entry misses a field");
                continue;
            }
            set.push_back(std::move(entry));
        }
        return set;
    }

    bool Apply(std::vector<ConstantReturn> const& set)
    {
        std::lock_guard guard(g_lock);
        bool changed = false;

        // First the functions that leave the set get their bytes back.
        for (auto it = g_patched.begin(); it != g_patched.end();)
        {
            bool kept = std::any_of(set.begin(), set.end(), [&](ConstantReturn const& entry) { return entry.site.id == it->id; });
            if (kept) { ++it; continue; }
            bool restored = Write(it->address, it->original);
            changed |= restored;
            deckle::Log(restored ? LogLevel::Info : LogLevel::Error, restored ? L"constant_return_restored" : L"constant_return_restore_failed",
                        restored ? L"Gave a function of explorer its own code back" : L"Could not give a function of explorer its own code back",
                        { { L"id", it->id } });
            it = g_patched.erase(it);
        }

        for (ConstantReturn const& entry : set)
        {
            std::wstring reason;
            uint8_t* address = Locate(entry.site, reason);
            if (!address)
            {
                Report(LogLevel::Warning, L"constant_return_skipped", L"Left a function of explorer alone: it is not the one Deckle resolved", entry, reason);
                continue;
            }

            Bytes current{};
            std::memcpy(current.data(), address, PatchLength);
            if (current != entry.site.original && !IsOurPatch(current))
            {
                Report(LogLevel::Warning, L"constant_return_skipped", L"Left a function of explorer alone: its code is not what Deckle read", entry, L"unexpected_bytes");
                continue;
            }

            const Bytes patch = PatchFor(entry.value);
            if (current != patch)
            {
                if (!Write(address, patch))
                {
                    Report(LogLevel::Error, L"constant_return_failed", L"Could not write the constant into a function of explorer", entry, L"write_failed");
                    continue;
                }
                changed = true;
            }

            auto known = std::find_if(g_patched.begin(), g_patched.end(), [&](Patched const& p) { return p.id == entry.site.id; });
            if (known == g_patched.end()) g_patched.push_back({ entry.site.id, address, entry.site.original });
            Report(LogLevel::Info, L"constant_return_applied", L"A function of explorer now returns a constant", entry);
        }
        return changed;
    }

    size_t RestoreAll()
    {
        std::lock_guard guard(g_lock);
        size_t restored = 0;
        for (Patched const& patched : g_patched)
        {
            if (Write(patched.address, patched.original)) ++restored;
            else deckle::Log(LogLevel::Error, L"constant_return_restore_failed", L"Could not give a function of explorer its own code back", { { L"id", patched.id } });
        }
        g_patched.clear();
        return restored;
    }
}
