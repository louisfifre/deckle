#include "pch.h"
#include "constant_return.h"
#include "../log.h"

#include <cstring>
#include <tlhelp32.h>

namespace
{
    namespace json = winrt::Windows::Data::Json;
    using deckle::patches::ConstantReturn;

    constexpr size_t PatchLength = 16;
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

    // Every other thread of explorer, held still while 16 bytes of code change
    // under them. A thread stopped inside those bytes would resume in the middle
    // of an instruction that is no longer there, so the write waits until none is.
    // Nothing here allocates or takes a lock once a thread is suspended: the one
    // held could be the heap's.
    class OtherThreadsHeld
    {
    public:
        OtherThreadsHeld()
        {
            std::vector<DWORD> ids;
            HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
            if (snapshot == INVALID_HANDLE_VALUE) return;
            THREADENTRY32 entry{ sizeof entry };
            const DWORD process = GetCurrentProcessId(), self = GetCurrentThreadId();
            for (BOOL more = Thread32First(snapshot, &entry); more; more = Thread32Next(snapshot, &entry))
            {
                if (entry.th32OwnerProcessID == process && entry.th32ThreadID != self) ids.push_back(entry.th32ThreadID);
            }
            CloseHandle(snapshot);

            m_threads.reserve(ids.size());
            for (DWORD id : ids)
            {
                HANDLE thread = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT, FALSE, id);
                if (!thread) continue;
                if (SuspendThread(thread) == static_cast<DWORD>(-1)) { CloseHandle(thread); continue; }
                m_threads.push_back(thread);
            }
        }

        ~OtherThreadsHeld()
        {
            for (HANDLE thread : m_threads) { ResumeThread(thread); CloseHandle(thread); }
        }

        OtherThreadsHeld(OtherThreadsHeld const&) = delete;
        OtherThreadsHeld& operator=(OtherThreadsHeld const&) = delete;

        bool NoneInside(uint8_t const* begin) const
        {
            const auto low = reinterpret_cast<DWORD64>(begin), high = low + PatchLength;
            for (HANDLE thread : m_threads)
            {
                CONTEXT context{};
                context.ContextFlags = CONTEXT_CONTROL;
                if (GetThreadContext(thread, &context) && context.Rip > low && context.Rip < high) return false;
            }
            return true;
        }

    private:
        std::vector<HANDLE> m_threads;
    };

    // Writes the bytes over live code. A thread about to run the first byte is
    // fine: it will run the new instruction whole. Only one stopped past it is not.
    bool Write(uint8_t* address, Bytes const& bytes)
    {
        for (int attempt = 0; attempt < 50; ++attempt)
        {
            {
                OtherThreadsHeld held;
                if (held.NoneInside(address))
                {
                    DWORD previous = 0;
                    if (!VirtualProtect(address, PatchLength, PAGE_EXECUTE_READWRITE, &previous)) return false;
                    std::memcpy(address, bytes.data(), PatchLength);
                    VirtualProtect(address, PatchLength, previous, &previous);
                    FlushInstructionCache(GetCurrentProcess(), address, PatchLength);
                    return true;
                }
            }
            Sleep(1);
        }
        return false;
    }

    // The function's address in memory, once the module there is the file Deckle
    // read and the address is the start of a function at least 16 bytes long.
    // Empty with the reason otherwise.
    uint8_t* Locate(ConstantReturn const& entry, std::wstring& reason)
    {
        HMODULE module = GetModuleHandleW(entry.module.c_str());
        if (!module) { reason = L"module_not_loaded"; return nullptr; }

        auto* base = reinterpret_cast<uint8_t*>(module);
        auto* dos = reinterpret_cast<IMAGE_DOS_HEADER const*>(base);
        auto* nt = reinterpret_cast<IMAGE_NT_HEADERS const*>(base + dos->e_lfanew);
        if (nt->FileHeader.TimeDateStamp != entry.timestamp || nt->OptionalHeader.SizeOfImage != entry.imageSize)
        {
            reason = L"other_build";
            return nullptr;
        }

        uint8_t* address = base + entry.rva;
        DWORD64 imageBase = 0;
        PRUNTIME_FUNCTION function = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(address), &imageBase, nullptr);
        if (!function || function->BeginAddress != entry.rva || function->EndAddress - function->BeginAddress < PatchLength)
        {
            reason = L"not_a_function_start";
            return nullptr;
        }
        return address;
    }

    std::wstring Number(double value)
    {
        wchar_t text[32];
        swprintf_s(text, L"%g", value);
        return text;
    }

    bool ReadHex(std::wstring_view hex, Bytes& bytes)
    {
        if (hex.size() != PatchLength * 2) return false;
        for (size_t i = 0; i < PatchLength; ++i)
        {
            auto digit = [](wchar_t c) -> int
            {
                if (c >= L'0' && c <= L'9') return c - L'0';
                if (c >= L'A' && c <= L'F') return c - L'A' + 10;
                if (c >= L'a' && c <= L'f') return c - L'a' + 10;
                return -1;
            };
            int high = digit(hex[2 * i]), low = digit(hex[2 * i + 1]);
            if (high < 0 || low < 0) return false;
            bytes[i] = static_cast<uint8_t>(high * 16 + low);
        }
        return true;
    }

    void Report(deckle::LogLevel level, std::wstring_view event, std::wstring_view message, ConstantReturn const& entry,
             std::wstring_view reason = {})
    {
        deckle::Log(level, event, message,
                    { { L"id", entry.id }, { L"module", entry.module }, { L"value", Number(entry.value) },
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
            auto text = [&](std::wstring_view key)
            {
                return object.HasKey(key) && object.GetNamedValue(key).ValueType() == json::JsonValueType::String
                    ? std::wstring(object.GetNamedString(key)) : std::wstring();
            };
            auto number = [&](std::wstring_view key, double& value)
            {
                if (!object.HasKey(key) || object.GetNamedValue(key).ValueType() != json::JsonValueType::Number) return false;
                value = object.GetNamedNumber(key);
                return true;
            };

            ConstantReturn entry;
            entry.id = text(L"id");
            entry.module = text(L"module");
            double timestamp = 0, imageSize = 0, rva = 0;
            bool complete = !entry.id.empty() && !entry.module.empty()
                && number(L"timestamp", timestamp) && number(L"imageSize", imageSize) && number(L"rva", rva)
                && number(L"value", entry.value) && ReadHex(text(L"original"), entry.original);
            if (!complete) { problems.push_back(L"constantReturns: an entry misses a field"); continue; }

            entry.timestamp = static_cast<uint32_t>(timestamp);
            entry.imageSize = static_cast<uint32_t>(imageSize);
            entry.rva = static_cast<uint32_t>(rva);
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
            bool kept = std::any_of(set.begin(), set.end(), [&](ConstantReturn const& entry) { return entry.id == it->id; });
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
            uint8_t* address = Locate(entry, reason);
            if (!address)
            {
                Report(LogLevel::Warning, L"constant_return_skipped", L"Left a function of explorer alone: it is not the one Deckle resolved", entry, reason);
                continue;
            }

            Bytes current{};
            std::memcpy(current.data(), address, PatchLength);
            if (current != entry.original && !IsOurPatch(current))
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

            auto known = std::find_if(g_patched.begin(), g_patched.end(), [&](Patched const& p) { return p.id == entry.id; });
            if (known == g_patched.end()) g_patched.push_back({ entry.id, address, entry.original });
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
