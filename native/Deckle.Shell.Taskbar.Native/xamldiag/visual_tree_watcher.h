#pragma once

namespace deckle::xamldiag
{
    // Receives every change of the XAML visual tree of one explorer.exe XAML
    // thread, through the XAML Diagnostics visual tree service. On Add the
    // callback runs on the UI thread that owns the element, which is the only
    // thread allowed to read it.
    //
    // The first Advise replays the whole live tree as Add events, parents
    // before children. That replay is how the DLL learns a tree that existed
    // before it connected. (Inferred from the TranslucentTB and Windhawk sources,
    // not written in Microsoft documentation; the harness run confirms it.)
    struct VisualTreeWatcher : winrt::implements<VisualTreeWatcher, IVisualTreeServiceCallback2, winrt::non_agile>
    {
        explicit VisualTreeWatcher(winrt::com_ptr<IUnknown> site);
        ~VisualTreeWatcher();

        VisualTreeWatcher(const VisualTreeWatcher&) = delete;
        VisualTreeWatcher& operator=(const VisualTreeWatcher&) = delete;

        // Starts listening. AdviseVisualTreeChange hangs when called from the
        // UI thread (it waits for that thread inside Advising::RunOnUIThread),
        // so it runs on a thread of its own.
        void Advise();

        // Stops listening and waits for the advising thread. Any thread but the UI thread.
        void Unadvise();

    private:
        HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutationType) override;
        HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle element, VisualElementState elementState, LPCWSTR context) noexcept override;

        void ReleaseInstance(InstanceHandle handle);

        winrt::com_ptr<IXamlDiagnostics> m_diagnostics;
        winrt::com_ptr<IUnknown> m_testHooks;  // IXamlDiagnosticsTestHooks, when the host has it
        std::thread m_adviseThread;
        std::atomic<bool> m_advised{false};
        std::atomic<bool> m_stopped{false};
    };

    // The one watcher this DLL keeps. Attach replaces the previous one (a new
    // XAML core in the same process hands over a new site); Detach unadvises
    // and drops it. Both are safe to call from any thread but the UI thread.
    void AttachWatcher(winrt::com_ptr<IUnknown> site);
    void DetachWatcher();
}
