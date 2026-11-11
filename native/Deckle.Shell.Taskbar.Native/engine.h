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

    // Starts the engine inside explorer.exe: pins this DLL, claims the process
    // (residency.h), then starts the worker thread that owns the channel to
    // Deckle. When another copy already holds the process, wakes it for this
    // copy's Deckle or tells that Deckle it is refused, and never touches XAML.
    // Called from the hook procedure, on explorer's own thread, once per
    // injection; a later call on the same copy wakes it when it is parked.
    void Start();

    // Asks the worker to end the session: it restores everything, then parks
    // (or unloads, while XAML Diagnostics never answered). Any thread.
    void RequestStop(std::wstring_view reason);

    bool IsStopping();
}
