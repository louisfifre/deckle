#pragma once

namespace deckle::xamldiag
{
    // Asks explorer's XAML runtime to load this DLL as a TAP (a diagnostics
    // plug-in). Returns the last HRESULT; S_OK means the runtime accepted the
    // connection, and the TAP's SetSite follows on its own.
    HRESULT ConnectToXamlDiagnostics();
}
