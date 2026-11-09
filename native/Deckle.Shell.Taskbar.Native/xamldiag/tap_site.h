#pragma once

namespace deckle::xamldiag
{
    // {2E6B1F0A-94C3-4D58-A1E7-5B03C8D7F214}
    inline constexpr CLSID CLSID_DeckleTaskbarTap =
        { 0x2e6b1f0a, 0x94c3, 0x4d58, { 0xa1, 0xe7, 0x5b, 0x03, 0xc8, 0xd7, 0xf2, 0x14 } };

    // The object InitializeXamlDiagnosticsEx instantiates inside explorer.exe
    // (it asks this DLL for the class factory with DllGetClassObject, then
    // gives the object a site: the IXamlDiagnostics of the XAML core).
    class TapSite : public winrt::implements<TapSite, IObjectWithSite, winrt::non_agile>
    {
    public:
        HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override;
        HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** ppvSite) noexcept override;

    private:
        winrt::com_ptr<IUnknown> m_site;
    };
}
