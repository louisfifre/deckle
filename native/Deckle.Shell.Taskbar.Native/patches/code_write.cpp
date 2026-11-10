#include "pch.h"
#include "code_write.h"
#include "../engine.h"

#include <cstring>
#include <tlhelp32.h>

namespace
{
    using deckle::patches::CodeRange;

    // Every other thread of explorer, held still while code changes under them.
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

        // No thread past the first byte of [first, end), none anywhere in `busy`.
        bool NoneInside(uintptr_t first, uintptr_t end, std::initializer_list<CodeRange> busy) const
        {
            for (HANDLE thread : m_threads)
            {
                CONTEXT context{};
                context.ContextFlags = CONTEXT_CONTROL;
                if (!GetThreadContext(thread, &context)) continue;
                if (context.Rip > first && context.Rip < end) return false;
                for (CodeRange const& range : busy)
                {
                    if (context.Rip >= range.begin && context.Rip < range.end) return false;
                }
            }
            return true;
        }

    private:
        std::vector<HANDLE> m_threads;
    };
}

namespace deckle::patches
{
    bool WriteCode(uint8_t* address, uint8_t const* bytes, size_t length, std::initializer_list<CodeRange> busy)
    {
        const auto first = reinterpret_cast<uintptr_t>(address);
        for (int attempt = 0; attempt < 50; ++attempt)
        {
            {
                OtherThreadsHeld held;
                if (held.NoneInside(first, first + length, busy))
                {
                    DWORD previous = 0;
                    if (!VirtualProtect(address, length, PAGE_EXECUTE_READWRITE, &previous)) return false;
                    std::memcpy(address, bytes, length);
                    VirtualProtect(address, length, previous, &previous);
                    FlushInstructionCache(GetCurrentProcess(), address, length);
                    return true;
                }
            }
            Sleep(1);
        }
        return false;
    }

    CodeRange OwnImage()
    {
        auto* base = reinterpret_cast<uint8_t const*>(deckle::engine::Module());
        if (!base) return {};
        auto* dos = reinterpret_cast<IMAGE_DOS_HEADER const*>(base);
        auto* nt = reinterpret_cast<IMAGE_NT_HEADERS const*>(base + dos->e_lfanew);
        const auto begin = reinterpret_cast<uintptr_t>(base);
        return { begin, begin + nt->OptionalHeader.SizeOfImage };
    }
}
