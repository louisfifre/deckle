#pragma once

namespace deckle::xamldiag
{
    // Asks explorer's XAML runtime to load this DLL as a TAP (a diagnostics
    // plug-in). Returns the last HRESULT; S_OK means the runtime accepted the
    // connection, and the TAP's SetSite follows on its own.
    // Runs at most once per process: S_FALSE without a call when it already
    // answered.
    HRESULT ConnectToXamlDiagnostics();

    // Whether InitializeXamlDiagnosticsEx ever answered in this process with
    // something other than "no such connection". From then on this DLL never
    // leaves the process: unloading would destroy the diagnostics, and the
    // next copy would have to initialize them a second time.
    bool DiagnosticsAnswered();
}
