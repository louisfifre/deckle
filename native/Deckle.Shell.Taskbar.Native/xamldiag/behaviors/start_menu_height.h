#pragma once

#include "../rules/property_override.h"

namespace deckle::behaviors
{
    // The Start menu height behavior. The new Start menu ("StartBlendedFlexFrame")
    // is a Grid#FrameRoot whose height Windows caps (MaxHeight, a MaxHeightEnforcer
    // rectangle, the StartSizes visual state group). This behavior makes FrameRoot
    // as high as the Start window itself, whatever the scale of the screen: the
    // window's size is read live in DIPs, so 100 %, 125 % and 150 % all come out
    // as "the whole height" without a number being stored anywhere.
    //
    // One hold per FrameRoot, on its UI thread. It
    //   - measures: the window's height (Window.Bounds, effective pixels) minus
    //     FrameRoot's own top and bottom margin, which stays native;
    //   - holds Height, MinHeight and MaxHeight at that value with
    //     PropertyOverrides (so a write of Explorer's code or a visual state
    //     is answered, and Restore gives the original values back);
    //   - measures again when the window becomes visible (on the next
    //     LayoutUpdated, once the menu has been laid out), and when the window
    //     or the frame changes size (a change of scale or of the screen).
    class StartMenuHeightHold : public std::enable_shared_from_this<StartMenuHeightHold>
    {
    public:
        // Null unless `element` is the Start menu's FrameRoot.
        static std::shared_ptr<StartMenuHeightHold> TryCreate(
            winrt::Windows::UI::Xaml::FrameworkElement const& element,
            std::shared_ptr<rules::OverrideCounters> counters);

        ~StartMenuHeightHold();

        // Stops listening and gives every property back. UI thread.
        void Release();

        bool IsTargetGone() const;
        void* TargetIdentity() const { return m_identity; }

        // Whether `element` is the Start menu's FrameRoot.
        static bool IsFrameRoot(winrt::Windows::UI::Xaml::FrameworkElement const& element);

    private:
        StartMenuHeightHold() = default;
        void Attach(winrt::Windows::UI::Xaml::FrameworkElement const& frameRoot);
        void Update(const wchar_t* reason);
        void OnVisible();
        void ReassertState();

        winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> m_frameRoot;
        winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> m_host;
        winrt::weak_ref<winrt::Windows::UI::Xaml::Window> m_window;
        winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> m_content;
        void* m_identity = nullptr;
        std::shared_ptr<rules::OverrideCounters> m_counters;
        std::vector<std::shared_ptr<rules::PropertyOverride>> m_overrides;
        winrt::event_token m_visibilityToken{};
        winrt::event_token m_windowSizeToken{};
        winrt::event_token m_hostSizeToken{};
        winrt::event_token m_layoutToken{};
        bool m_layoutPending = false;
        bool m_applied = false;
        bool m_released = false;
        double m_height = 0;
        double m_lastLogged = -1;
        double m_originalMax = 0;   // FrameRoot's effective MaxHeight when the hold began (the visual state's, when it owns it)
    };
}
