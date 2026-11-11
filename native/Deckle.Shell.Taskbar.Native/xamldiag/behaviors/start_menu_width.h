#pragma once

#include "../rules/property_override.h"

namespace deckle::behaviors
{
    // The Start menu width behavior. The new Start menu ("StartBlendedFlexFrame")
    // is a Grid#FrameRoot whose width follows its content: AnimationRoot, and in
    // it Grid#MainMenu, the panel the person sees, which Windows gives a local
    // Width (834 DIP on 26100, 858 with the room for its shadow). This behavior
    // holds MainMenu's Width, MinWidth and MaxWidth at one value: the width asked for, or the widest the Start window
    // allows ("fill"), and never wider than that, so a value meant for a wide
    // screen still fits a portrait one.
    //
    // Every figure is measured on the live tree, in effective pixels (DIP):
    //   - the room around the panel inside FrameRoot (its shadow, 12 DIP on each
    //     side on 26100), FrameRoot's ActualWidth less MainMenu's;
    //   - the window's width, and FrameRoot's left and right margins in it.
    // The widest panel is then the window less FrameRoot's margins and that room,
    // so a filled menu keeps Windows' own gap on both sides.
    //
    // The value is held by a PropertyOverride (a rewrite of Windows' code is
    // answered; Release gives the original back). It is measured again when the
    // window changes size (the menu opened on another screen) and each time the
    // menu shows.
    class StartMenuWidthHold : public std::enable_shared_from_this<StartMenuWidthHold>
    {
    public:
        // Null unless `element` is the Start menu's FrameRoot and its MainMenu is there.
        // `width` is the panel's width in DIP; ignored when `fill`.
        static std::shared_ptr<StartMenuWidthHold> TryCreate(
            winrt::Windows::UI::Xaml::FrameworkElement const& element,
            bool fill, double width,
            std::shared_ptr<rules::OverrideCounters> counters);

        ~StartMenuWidthHold();

        // Stops listening and gives the width back. UI thread.
        void Release();

        bool IsTargetGone() const;
        void* TargetIdentity() const { return m_identity; }

        // The width the panel gets: the one asked for (or all the room when
        // filling), held to the room there is. Pure, so the rule reads in one place.
        static double Fit(bool fill, double wanted, double room);

    private:
        StartMenuWidthHold() = default;
        void Attach(winrt::Windows::UI::Xaml::FrameworkElement const& frameRoot,
                    winrt::Windows::UI::Xaml::FrameworkElement const& menu);
        void Update(const wchar_t* reason);
        void AfterNextLayout(void (StartMenuWidthHold::*step)());
        void OnLaidOutVisible();
        void Verify();

        winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> m_frameRoot;
        winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> m_menu;
        winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> m_host;
        winrt::weak_ref<winrt::Windows::UI::Xaml::Window> m_window;
        winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> m_content;
        void* m_identity = nullptr;
        std::shared_ptr<rules::OverrideCounters> m_counters;
        std::vector<std::shared_ptr<rules::PropertyOverride>> m_overrides;   // Width, MinWidth, MaxWidth
        winrt::event_token m_visibilityToken{};
        winrt::event_token m_windowSizeToken{};
        winrt::event_token m_hostSizeToken{};
        winrt::event_token m_layoutToken{};
        bool m_layoutPending = false;
        bool m_applied = false;
        bool m_released = false;
        bool m_fill = false;
        double m_wanted = 0;    // the width asked for, in DIP
        double m_room = -1;     // the room around the panel inside FrameRoot, measured before any change
        double m_width = 0;     // MainMenu's held Width
    };
}
