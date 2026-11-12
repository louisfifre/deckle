#pragma once

namespace deckle::xamldiag
{
    // Keeps the XAML composition diagnostics from being created in explorer.
    //
    // Those diagnostics rebuild a process-wide visual tree walker without any
    // locking each time a DirectComposition visual is added, so two UI threads
    // adding visuals at once corrupt the heap. Explorer runs several XAML
    // threads (one per DesktopWindowXamlSource: the taskbar, its flyouts, the
    // thumbnails), and a corrupted heap fails fast in dcomp.dll (0xc0000602).
    // The engine only needs element mutations, which come from another path.
    //
    // XamlDiagnostics::CreateCompVisualDiag skips them when
    // HKLM\Software\Microsoft\XAML\Debug\DisableCompositionDiag is 1, and
    // Windows.UI.Xaml.dll reads that value once per process, from within the
    // first AdviseVisualTreeChange, on the calling thread. So Windows.UI.Xaml.dll's
    // registry imports are redirected here, and they answer 1 for that value on
    // a thread inside a Scope and pass every other call through unchanged.
    // (Mechanism from Windhawk's windows-11-taskbar-styler, which answers the
    // same read by hooking RegOpenKeyExW and RegQueryValueExW; the key name is
    // in the Windows.UI.Xaml.dll binary.)
    //
    // The redirection is installed once and never removed, and Install pins
    // this DLL for the life of the process, so the hooks it points to never go
    // away. In explorer that matches residency.h: once XAML Diagnostics
    // answered, the DLL parks there instead of unloading.
    class CompositionDiagGuard
    {
    public:
        // Redirects the imports of the loaded Windows.UI.Xaml.dll. True when
        // the value read is now answered, by the open and query pair or by
        // RegGetValueW. Idempotent and safe from any thread.
        static bool Install();

        // Whether a read of DisableCompositionDiag was answered with 1 since
        // the process started.
        static bool Answered();

        // While alive, this thread's reads of DisableCompositionDiag answer 1.
        class Scope
        {
        public:
            Scope();
            ~Scope();
            Scope(Scope const&) = delete;
            Scope& operator=(Scope const&) = delete;
        };
    };
}
