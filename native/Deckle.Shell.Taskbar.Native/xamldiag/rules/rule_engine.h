#pragma once

#include "rule_document.h"

namespace deckle::rules
{
    // The tier-2 engine: holds the document's rules over the live XAML tree of
    // explorer.exe.
    //
    // Threads. A XAML element may only be touched on the UI thread that owns it,
    // and explorer runs more than one (the taskbar island, the tray, other
    // monitors' bars). So the engine keeps one ThreadContext per UI thread, whose
    // lists of elements and overrides only that thread reads or writes, and
    // reaches a context from any other thread by running a function on its
    // dispatcher. Nothing XAML is ever released on the wrong thread either:
    // dropping the last reference to an element on another thread would run its
    // destructor there.
    //
    // Flow.
    //   OnElementAdded    the visual tree watcher reports an element (its thread).
    //                     It is remembered, and each rule that targets it applies.
    //   Apply             a new document (the worker thread): every override is
    //                     restored, the rules are swapped, and the remembered
    //                     elements are walked again. The tree is not re-advised:
    //                     the engine already holds every element the watcher
    //                     reported, so a new document costs one hop per UI thread.
    //   Shutdown          restores everything and forgets the elements.
    //
    // Status. The engine posts a "status" record over the channel after each
    // document and whenever a rule's numbers change: for each rule how many
    // elements it matched, how many overrides are in force, how many times
    // Explorer's code undid it and how many times that went beyond the budget.
    // A rule that matched nothing 10 seconds after its document is reported as
    // never matched (an element that shows up later, such as the tray's
    // overflow, simply makes it matched).
    class Engine
    {
    public:
        static Engine& Instance();

        // The element's UI thread: the watcher's callback.
        void OnElementAdded(winrt::Windows::UI::Xaml::FrameworkElement const& element);

        // The worker thread. Waits for the UI threads (a few seconds at most each).
        void Apply(SettingsDocument document);

        // Restores everything the engine changed and stops its timer thread.
        // Returns how many overrides it gave back. The worker thread.
        size_t Shutdown();

        // Posts the live visual trees as "tree" records, then "tree_end". The worker thread.
        void DumpTrees();

    private:
        Engine();
        struct Impl;
        Impl* m_impl;
    };
}
