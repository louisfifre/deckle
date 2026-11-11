#pragma once

namespace deckle::residency
{
    // One engine per process, for the life of the process.
    //
    // Why. XAML Diagnostics keep process-wide state behind the IXamlDiagnostics
    // a TAP is handed: a singleton that points, without a reference, at the
    // current XamlDiagnostics object, and a runtime feature flag. Destroying a
    // XamlDiagnostics object tears that state down for the whole process
    // (microsoft-ui-xaml XamlDiagnostics.cpp, the destructor; Windhawk's
    // taskbar styler warns about the same ordering). So a second
    // InitializeXamlDiagnosticsEx in a process that already had one, after the
    // first engine unloaded or beside a live one, left explorer to fail fast in
    // Windows.UI.Xaml.dll (0xc000027b): five crashes on 2026-11-10 and 11, all
    // in an explorer that hosted the engine for the second time, none in a
    // fresh one (ExplorerReinjectionBugNotes.md). TranslucentTB never
    // initializes twice either: its TAP stays pinned and its app reconnects.
    //
    // How. The first copy of this DLL that starts in a process claims it with
    // a named block of shared memory, Local\Deckle.Taskbar.Engine.<pid>, and
    // keeps it until the process ends. Any later copy (the same file injected
    // again, a newer build, the development console's own copy) finds the
    // claim and never touches XAML: when the owner is parked it writes the pipe
    // its Deckle serves into the block and wakes the owner, which connects to
    // that pipe; when the owner is serving another Deckle it is refused.
    enum class Claim
    {
        Owner,        // this copy is the process's engine
        HandedOver,   // the parked owner was asked to serve this copy's pipe
        Busy,         // the owner is serving another Deckle: this copy must leave
    };

    // On the hook's thread, once per copy. `pipe` is the pipe this copy's
    // Deckle serves.
    Claim ClaimOrHandOver(std::wstring const& pipe);

    // The owner asks itself to serve `pipe`: Deckle injected the owner's own
    // file again. False when the owner is not parked.
    bool HandOverToSelf(std::wstring const& pipe);

    // False when the shared block could not be made, so nobody can wake the
    // owner once it parks (it then stays idle until explorer restarts).
    bool CanBeWoken();

    // The owner, from its worker. Parks, then blocks until a copy hands it a
    // pipe, and returns that pipe's name with the owner marked as serving.
    std::wstring ParkUntilHandedOver();

    // The owner leaves the process before XAML Diagnostics ever ran (no Deckle
    // on the pipe): the next copy may claim the process.
    void Release();
}
