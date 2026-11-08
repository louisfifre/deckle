#include "pch.h"
#include "engine.h"

// The injection route, the one TranslucentTB uses. Deckle loads its own copy of
// this DLL, installs a WH_CALLWNDPROC hook on the thread that owns
// Shell_TrayWnd with this procedure, and sends that window one registered
// message. Windows maps the DLL into explorer.exe to run the procedure there,
// which is the documented purpose of a hook procedure living in a DLL. No
// process scan, no remote thread.
//
// The procedure runs on explorer's taskbar thread, inside a SendMessage: it
// only starts the engine's worker and returns.
extern "C" LRESULT CALLBACK DeckleTaskbarHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    if (code == HC_ACTION)
    {
        const auto* message = reinterpret_cast<const CWPSTRUCT*>(lParam);
        if (message->message == deckle::engine::InjectMessage())
        {
            deckle::engine::Start();
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

// A COM server needs this pair; the XAML Diagnostics host asks the TAP DLL for
// its class factory through DllGetClassObject (tap_site.cpp). The module is
// unloadable while any C++/WinRT object it created is alive.
extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return winrt::get_module_lock() ? S_FALSE : S_OK;
}
