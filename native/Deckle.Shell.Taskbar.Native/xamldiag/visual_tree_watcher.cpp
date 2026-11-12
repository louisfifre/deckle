#include "pch.h"
#include "visual_tree_watcher.h"
#include "composition_diag_guard.h"
#include "element_properties.h"
#include "rules/rule_engine.h"
#include "../log.h"
#include "../engine.h"
#include "../util.h"

// Adapted from TranslucentTB's ExplorerTAP (visualtreewatcher.cpp) and
// Windhawk's windows-11-taskbar-styler mod (VisualTreeWatcher), both GPLv3:
//   https://github.com/TranslucentTB/TranslucentTB/tree/develop/ExplorerTAP
//   https://github.com/ramensoftware/windhawk-mods/blob/main/mods/windows-11-taskbar-styler.wh.cpp
// See NOTICE.md.

namespace
{
    namespace wf = winrt::Windows::Foundation;

    // XamlDiagnostics implements this interface too and xamlom.h does not
    // declare it. UnregisterInstance closes the runtime object the diagnostics
    // cache for a handle, the only reference they keep to an element once it
    // was reported. Without it every element ever reported stays alive.
    constexpr GUID IID_IXamlDiagnosticsTestHooks =
        { 0x735941a2, 0x3ee3, 0x495a, { 0x8d, 0xa9, 0x97, 0x26, 0x27, 0x00, 0x30, 0x75 } };

    struct IXamlDiagnosticsTestHooks : IUnknown
    {
        virtual HRESULT STDMETHODCALLTYPE UnregisterInstance(InstanceHandle handle) = 0;
        virtual HRESULT STDMETHODCALLTYPE TryGetDispatcherQueueForObject(InstanceHandle handle, void** dispatcherQueue) = 0;
    };

    // The callback owns the strings it is handed.
    struct Bstr
    {
        BSTR value;
        explicit Bstr(BSTR v) : value(v) {}
        ~Bstr() { SysFreeString(value); }
        Bstr(const Bstr&) = delete;
        Bstr& operator=(const Bstr&) = delete;
        std::wstring Text() const { return value ? std::wstring(value, SysStringLen(value)) : std::wstring(); }
    };

    std::mutex g_watcherLock;
    winrt::com_ptr<deckle::xamldiag::VisualTreeWatcher> g_watcher;
}

namespace deckle::xamldiag
{
    VisualTreeWatcher::VisualTreeWatcher(winrt::com_ptr<IUnknown> site)
        : m_diagnostics(site.as<IXamlDiagnostics>())
    {
        HRESULT hr = m_diagnostics->QueryInterface(IID_IXamlDiagnosticsTestHooks, m_testHooks.put_void());
        if (FAILED(hr))
        {
            Log(LogLevel::Warning, L"test_hooks_missing",
                L"The XAML diagnostics do not offer IXamlDiagnosticsTestHooks, so elements stay referenced",
                { { L"hr", util::HandleText(static_cast<uint32_t>(hr)) } });
        }
    }

    VisualTreeWatcher::~VisualTreeWatcher()
    {
        // The advising thread holds a strong reference, so it is finished by now.
        if (m_adviseThread.joinable()) m_adviseThread.detach();
    }

    void VisualTreeWatcher::Advise()
    {
        // The thread keeps this object alive until Advise returns.
        m_adviseThread = std::thread([self = get_strong()]
        {
            // The composition diagnostics corrupt explorer's heap when two UI
            // threads add DirectComposition visuals at once, and explorer then
            // fails fast in dcomp.dll. They are switched off for the process by
            // the read this call makes (composition_diag_guard.h).
            // TODO (hooks step): CreateWindowExW / CreateWindowInBand hooks, to
            // catch XAML windows created after this point.
            const bool guarded = CompositionDiagGuard::Install();
            HRESULT hr;
            {
                CompositionDiagGuard::Scope scope;
                self->m_adviseCalled = true;
                hr = self->m_diagnostics.as<IVisualTreeService3>()->AdviseVisualTreeChange(self.get());
            }
            if (CompositionDiagGuard::Answered())
            {
                Log(LogLevel::Info, L"composition_diag_disabled",
                    L"The XAML composition diagnostics are off in this process");
            }
            else
            {
                // Unanswered: the guard could not be installed, or the value
                // was read before it, by an earlier engine in this explorer.
                // Only a restart of explorer clears the cached value.
                Log(LogLevel::Warning, L"composition_diag_active",
                    L"The XAML composition diagnostics may still be on in this process",
                    { { L"guard_installed", guarded ? L"true" : L"false" } });
            }
            if (FAILED(hr))
            {
                Log(LogLevel::Error, L"advise_failed", L"AdviseVisualTreeChange failed",
                    { { L"hr", util::HandleText(static_cast<uint32_t>(hr)) } });
                return;
            }
            Log(LogLevel::Info, L"advised", L"Listening to the XAML visual tree");
        });
    }

    void VisualTreeWatcher::Unadvise()
    {
        m_stopped = true;
        if (m_adviseThread.joinable()) m_adviseThread.join();

        // Unadvised even when Advise reported a failure: on this build it can
        // return E_UNEXPECTED after the callbacks were already registered and
        // delivered, and a callback left registered keeps this DLL pinned.
        if (!m_adviseCalled.exchange(false)) return;

        HRESULT hr = m_diagnostics.as<IVisualTreeService3>()->UnadviseVisualTreeChange(this);
        if (FAILED(hr))
        {
            Log(LogLevel::Warning, L"unadvise_failed", L"UnadviseVisualTreeChange failed",
                { { L"hr", util::HandleText(static_cast<uint32_t>(hr)) } });
        }
        else
        {
            Log(LogLevel::Info, L"unadvised", L"Stopped listening to the XAML visual tree");
        }
    }

    // Release is deferred: the diagnostics cannot resolve the handle of a child
    // whose parent was already released (GetIInspectableFromHandle answers
    // ERROR_NOT_FOUND), and a parent is reported before its children. So the
    // handles are queued, and the queue is released at the first callback after
    // the tree has been quiet for a moment, on the UI thread of that callback,
    // as the styler does.
    void VisualTreeWatcher::QueueRelease(InstanceHandle handle)
    {
        if (!m_testHooks) return;
        std::lock_guard guard(m_releaseLock);
        m_pendingRelease.push_back(handle);
    }

    void VisualTreeWatcher::FlushReleasesIfQuiet()
    {
        const auto now = std::chrono::steady_clock::now();
        std::vector<InstanceHandle> pending;
        {
            std::lock_guard guard(m_releaseLock);
            bool quiet = now - m_lastCallback > std::chrono::milliseconds(750);
            m_lastCallback = now;
            if (!quiet || m_pendingRelease.empty()) return;
            pending.swap(m_pendingRelease);
        }

        auto hooks = static_cast<IXamlDiagnosticsTestHooks*>(m_testHooks.get());
        for (InstanceHandle handle : pending)
        {
            hooks->UnregisterInstance(handle);
        }
        Log(LogLevel::Verbose, L"references_released", L"Released the diagnostics references of reported elements",
            { { L"count", std::to_wstring(pending.size()) } });
    }

    HRESULT VisualTreeWatcher::OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutationType)
    {
        Bstr fileName(element.SrcInfo.FileName);
        Bstr hash(element.SrcInfo.Hash);
        Bstr name(element.Name);
        Bstr type(element.Type);

        // Always S_OK: an error here stops the diagnostics from sending
        // further changes. Nothing may escape either.
        try
        {
            if (m_stopped) return S_OK;
            FlushReleasesIfQuiet();

            if (mutationType == Add)
            {
                std::wstring props;
                try
                {
                    wf::IInspectable instance{ nullptr };
                    HRESULT hr = m_diagnostics->GetIInspectableFromHandle(
                        element.Handle, reinterpret_cast<::IInspectable**>(winrt::put_abi(instance)));
                    if (FAILED(hr) || !instance)
                    {
                        props = L"!inspectable:" + util::HandleText(static_cast<uint32_t>(hr));
                    }
                    else
                    {
                        props = DescribeElement(instance);

                        // The rule engine holds the element from here on, and
                        // applies the rules that target it. This is the
                        // element's own UI thread, the only one that may touch it.
                        if (auto framework = instance.try_as<winrt::Windows::UI::Xaml::FrameworkElement>())
                        {
                            try { rules::Engine::Instance().OnElementAdded(framework); }
                            catch (winrt::hresult_error const& error)
                            {
                                Log(LogLevel::Warning, L"engine_add_failed", L"The rule engine could not take an element",
                                    { { L"hr", util::HandleText(static_cast<uint32_t>(error.code().value)) }, { L"type", type.Text() } });
                            }
                        }
                    }
                }
                catch (winrt::hresult_error const& error)
                {
                    props = L"!throw:" + util::HandleText(static_cast<uint32_t>(error.code().value));
                }
                catch (...) { props = L"!throw"; }

                QueueRelease(element.Handle);

                Log(LogLevel::Verbose, L"visual_add", L"Element added to the visual tree",
                    {
                        { L"h", util::HandleText(element.Handle) },
                        { L"p", util::HandleText(relation.Parent) },
                        { L"i", std::to_wstring(relation.ChildIndex) },
                        { L"type", type.Text() },
                        { L"name", name.Text() },
                        { L"props", props },
                        { L"tid", std::to_wstring(GetCurrentThreadId()) },
                    });
            }
            else
            {
                // Only element.Handle is valid on a removal.
                QueueRelease(element.Handle);
                Log(LogLevel::Verbose, L"visual_remove", L"Element removed from the visual tree",
                    { { L"h", util::HandleText(element.Handle) } });
            }
        }
        catch (...) {}

        return S_OK;
    }

    HRESULT VisualTreeWatcher::OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept
    {
        return S_OK;
    }

    void AttachWatcher(winrt::com_ptr<IUnknown> site)
    {
        winrt::com_ptr<VisualTreeWatcher> previous;
        auto watcher = winrt::make_self<VisualTreeWatcher>(site);
        {
            std::lock_guard guard(g_watcherLock);
            previous = std::move(g_watcher);
            g_watcher = watcher;
        }
        // Outside the lock: unadvising waits on the UI thread.
        if (previous) previous->Unadvise();
        watcher->Advise();
    }

    void DetachWatcher()
    {
        winrt::com_ptr<VisualTreeWatcher> previous;
        {
            std::lock_guard guard(g_watcherLock);
            previous = std::move(g_watcher);
        }
        if (previous) previous->Unadvise();
    }
}



