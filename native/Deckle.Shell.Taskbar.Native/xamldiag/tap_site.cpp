#include "pch.h"
#include "tap_site.h"
#include "visual_tree_watcher.h"
#include "../engine.h"
#include "../log.h"
#include "../util.h"

// Adapted from TranslucentTB's ExplorerTAP (tapsite.cpp, module.cpp) and
// Windhawk's windows-11-taskbar-styler mod, both GPLv3. See NOTICE.md.

namespace
{
    template <typename T>
    struct SimpleFactory : winrt::implements<SimpleFactory<T>, IClassFactory, winrt::non_agile>
    {
        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** result) noexcept override
        try
        {
            if (outer) return CLASS_E_NOAGGREGATION;
            *result = nullptr;
            return winrt::make<T>().as(riid, result);
        }
        catch (...)
        {
            return winrt::to_hresult();
        }

        HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override { return S_OK; }
    };
}

namespace deckle::xamldiag
{
    HRESULT TapSite::SetSite(IUnknown* site)
    try
    {
        m_site.copy_from(site);

        if (!m_site)
        {
            // The XAML core is going away; it hands back no site.
            DetachWatcher();
            return S_OK;
        }

        // InitializeXamlDiagnosticsEx loaded this DLL once more to host the
        // TAP. That extra reference would keep the DLL mapped for the life of
        // explorer, so it is given back here, as the styler does. The DLL
        // stays loaded through the pin the engine took at start, and unloads
        // when the engine drops it.
        FreeLibrary(engine::Module());

        Log(LogLevel::Info, L"tap_site_set", L"Connected to XAML Diagnostics");
        AttachWatcher(m_site);
        return S_OK;
    }
    catch (...)
    {
        HRESULT hr = winrt::to_hresult();
        Log(LogLevel::Error, L"tap_site_failed", L"Could not set up the visual tree watcher",
            { { L"hr", util::HandleText(static_cast<uint32_t>(hr)) } });
        return hr;
    }

    HRESULT TapSite::GetSite(REFIID riid, void** ppvSite) noexcept
    {
        if (!m_site) return E_FAIL;
        return m_site->QueryInterface(riid, ppvSite);
    }
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, LPVOID* result)
try
{
    if (clsid == deckle::xamldiag::CLSID_DeckleTaskbarTap)
    {
        *result = nullptr;
        return winrt::make<SimpleFactory<deckle::xamldiag::TapSite>>().as(iid, result);
    }
    return CLASS_E_CLASSNOTAVAILABLE;
}
catch (...)
{
    return winrt::to_hresult();
}

