#include "pch.h"
#include "residency.h"

namespace
{
    // The block every copy in the process reads through the same name.
    // `state` moves Serving -> Parked (the owner, once its Deckle is gone) ->
    // Writing (one newcomer won the exchange) -> Requested (the pipe name is
    // written) -> Serving (the owner took it). Only the copy that moved the
    // state to Writing writes `pipe`, and the owner reads it only in Requested.
    struct Block
    {
        volatile LONG state;
        wchar_t pipe[256];
    };

    enum : LONG
    {
        Serving = 0,
        Parked = 1,
        Writing = 2,
        Requested = 3,
    };

    HANDLE g_mapping = nullptr;   // the owner's, kept until the process ends
    Block* g_block = nullptr;
    HANDLE g_wake = nullptr;      // auto-reset: a newcomer handed over a pipe

    // Named objects in Local\ are per session, and per package for an
    // AppContainer process (the Start menu host): the pid keeps two processes
    // of one session apart.
    std::wstring NameFor(std::wstring_view kind)
    {
        return L"Local\\Deckle.Taskbar." + std::wstring(kind) + L"." + std::to_wstring(GetCurrentProcessId());
    }

    bool HandOver(Block* block, std::wstring const& pipe)
    {
        if (InterlockedCompareExchange(&block->state, Writing, Parked) != Parked) return false;
        wcsncpy_s(block->pipe, pipe.c_str(), _TRUNCATE);
        InterlockedExchange(&block->state, Requested);

        HANDLE wake = OpenEventW(EVENT_MODIFY_STATE, FALSE, NameFor(L"Wake").c_str());
        if (wake)
        {
            SetEvent(wake);
            CloseHandle(wake);
        }
        return true;
    }
}

namespace deckle::residency
{
    Claim ClaimOrHandOver(std::wstring const& pipe)
    {
        HANDLE mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(Block),
                                            NameFor(L"Engine").c_str());
        const DWORD error = GetLastError();

        // No block at all: nothing can tell this copy apart from another, so it
        // acts alone, as every copy did before the claim existed.
        if (!mapping) return Claim::Owner;

        auto* block = static_cast<Block*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Block)));
        if (error != ERROR_ALREADY_EXISTS)
        {
            if (!block)
            {
                CloseHandle(mapping);
                return Claim::Owner;
            }
            // A new mapping is zeroed: the owner starts as Serving.
            g_mapping = mapping;
            g_block = block;
            g_wake = CreateEventW(nullptr, FALSE, FALSE, NameFor(L"Wake").c_str());
            return Claim::Owner;
        }

        // Another copy owns the process. Refused when its block cannot be read:
        // a second engine is what the claim exists to prevent.
        Claim result = block && HandOver(block, pipe) ? Claim::HandedOver : Claim::Busy;
        if (block) UnmapViewOfFile(block);
        CloseHandle(mapping);
        return result;
    }

    bool HandOverToSelf(std::wstring const& pipe)
    {
        return g_block && HandOver(g_block, pipe);
    }

    bool CanBeWoken()
    {
        return g_block && g_wake;
    }

    std::wstring ParkUntilHandedOver()
    {
        if (!CanBeWoken())
        {
            // Nobody can reach this engine: it stays as it is, idle, until the
            // process ends. The caller logged why before it got here.
            for (;;) Sleep(INFINITE);
        }

        InterlockedExchange(&g_block->state, Parked);
        for (;;)
        {
            WaitForSingleObject(g_wake, INFINITE);
            if (g_block->state != Requested) continue;   // a wake left over from an earlier hand-over
            std::wstring pipe(g_block->pipe);
            InterlockedExchange(&g_block->state, Serving);
            return pipe;
        }
    }

    void Release()
    {
        if (g_block) UnmapViewOfFile(g_block);
        if (g_mapping) CloseHandle(g_mapping);
        if (g_wake) CloseHandle(g_wake);
        g_block = nullptr;
        g_mapping = nullptr;
        g_wake = nullptr;
    }
}
