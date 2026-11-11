#include "pch.h"
#include "tap_connection.h"
#include "tap_site.h"
#include "../engine.h"
#include "../log.h"
#include "../util.h"

// Adapted from TranslucentTB's ExplorerTAP (tapsite.cpp Install) and Windhawk's
// windows-11-taskbar-styler mod (InjectWindhawkTAP), both GPLv3. See NOTICE.md.

namespace
{
    using InitializeXamlDiagnosticsExFn = decltype(&InitializeXamlDiagnosticsEx);

    std::atomic<bool> g_answered{ false };
}

namespace deckle::xamldiag
{
    bool DiagnosticsAnswered() { return g_answered.load(); }

    HRESULT ConnectToXamlDiagnostics()
    {
        // Once per process, ever (residency.h says why): a second call, from
        // this copy or any other, is what crashed explorer.
        if (g_answered) return S_FALSE;

        const std::wstring location = util::ModulePath(engine::Module());
        if (location.empty()) return HRESULT_FROM_WIN32(GetLastError());

        // The same load the XAML runtime does, to reach its exported function.
        HMODULE xaml = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!xaml) return HRESULT_FROM_WIN32(GetLastError());

        auto initialize = reinterpret_cast<InitializeXamlDiagnosticsExFn>(
            GetProcAddress(xaml, "InitializeXamlDiagnosticsEx"));
        if (!initialize) return HRESULT_FROM_WIN32(GetLastError());

        // The runtime names its diagnostics connections VisualDiagConnection1,
        // 2, ... and there is no way to ask which exists, so the names are
        // tried in turn until one is not ERROR_NOT_FOUND. Reference:
        // microsoft-ui-xaml DXamlCore.cpp, around line 2782.
        //
        // Each attempt runs on a fresh thread: XAML Diagnostics can be
        // initialized once per thread, and a second call from the same thread
        // returns S_OK without doing anything.
        HRESULT hr = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        const DWORD processId = GetCurrentProcessId();
        for (int attempt = 1; attempt <= 10000; ++attempt)
        {
            if (engine::IsStopping()) return E_ABORT;

            const std::wstring connection = L"VisualDiagConnection" + std::to_wstring(attempt);
            std::thread([&]
            {
                hr = initialize(connection.c_str(), processId, nullptr, location.c_str(),
                                CLSID_DeckleTaskbarTap, nullptr);
            }).join();

            if (hr != HRESULT_FROM_WIN32(ERROR_NOT_FOUND))
            {
                // Whatever the answer, the runtime went past the name check
                // and may have built its diagnostics: this process is spent.
                g_answered = true;
                deckle::Log(SUCCEEDED(hr) ? deckle::LogLevel::Info : deckle::LogLevel::Warning,
                            L"tap_connection", L"XAML Diagnostics answered",
                            { { L"connection", connection }, { L"hr", util::HandleText(static_cast<uint32_t>(hr)) } });
                return hr;
            }
        }
        return hr;
    }
}
