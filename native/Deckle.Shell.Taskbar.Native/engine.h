#pragma once

namespace deckle::engine
{
    // The module handle DllMain received, for the code that must name this DLL
    // (its path for the XAML Diagnostics TAP, its handle for FreeLibrary).
    void SetModule(HMODULE module);
    HMODULE Module();

    // The registered window message the hook procedure listens for. Deckle
    // sends it to Shell_TrayWnd while its hook is installed.
    UINT InjectMessage();

    // Starts the engine inside explorer.exe: pins this DLL, then starts the
    // worker thread that owns the channel to Deckle. Idempotent; called from
    // the hook procedure, on explorer's own thread.
    void Start();

    // Asks the worker to restore, unadvise and unload. Any thread.
    void RequestStop(std::wstring_view reason);

    bool IsStopping();
}
