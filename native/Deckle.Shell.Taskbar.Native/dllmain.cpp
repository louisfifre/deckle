#include "pch.h"
#include "engine.h"

// Nothing runs at load: the DLL is mapped into explorer.exe by the window hook
// (hook_entry.cpp) and its work starts from the hook procedure, never from
// here. DllMain runs under the loader lock, where starting threads, taking
// locks or calling COM can deadlock the whole process.
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        deckle::engine::SetModule(module);
    }
    return TRUE;
}

extern "C" int __stdcall DeckleTaskbarNativeVersion()
{
    // major * 10000 + minor * 100 + patch, the file version of version.rc.
    return 100;
}

