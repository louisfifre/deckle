#pragma once

#include "../rules/property_override.h"

namespace deckle::behaviors
{
    // The Start menu height behavior. The new Start menu ("StartBlendedFlexFrame")
    // is a Grid#FrameRoot whose height Windows caps (MaxHeight, a MaxHeightEnforcer
    // rectangle, the StartSizes visual state group). This behavior makes the visible
    // panel (FrameRoot's child AnimationRoot) as tall as the Start window allows,
    // with the same gap above and below it, at any screen size and scale.
    //
    // Nothing is assumed about the layout: every figure is measured on the live
    // tree, in effective pixels (DIP), the unit XAML lays out in.
    //   - The gap is the panel's shadow inset inside FrameRoot, the room Windows
    //     keeps around it and the gap it shows on the taskbar's side (12 DIP on
    //     26100), measured once.
    //   - The panel's insets inside FrameRoot (its shadow room) and FrameRoot's
    //     position in its parent are measured at each update.
    //   - From those, FrameRoot gets VerticalAlignment Top, a top margin that puts
    //     the panel at the gap, no bottom margin, and Height = MinHeight =
    //     MaxHeight that puts the panel's bottom at the gap from the window's
    //     bottom. No bottom margin and a top alignment, because XAML clips an
    //     element whose Height exceeds its layout slot, and centres one that
    //     fits a stretched slot: either would break the equal gaps.
    //   - After the next layout pass the result is measured again: both gaps and
    //     whether the layout slot clips FrameRoot. A mismatch is corrected once
    //     more; what remains is logged as start_menu_fit.
    // Each value is held by a PropertyOverride (a write of Explorer's code or a
    // visual state is answered; Restore gives the original back).
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
        // Where things are, in DIP, relative to the window's content.
        struct Measure
        {
            double window = 0;      // the window's height
            double hostTop = 0;     // FrameRoot's parent's top
            double frameTop = 0;    // FrameRoot's top
            double frameHeight = 0;
            double panelTop = 0;    // AnimationRoot's top
            double panelHeight = 0;
            double insetTop() const { return panelTop - frameTop; }
            double insetBottom() const { return frameTop + frameHeight - (panelTop + panelHeight); }
            double gapTop() const { return panelTop; }
            double gapBottom() const { return window - (panelTop + panelHeight); }
        };

        StartMenuHeightHold() = default;
        void Attach(winrt::Windows::UI::Xaml::FrameworkElement const& frameRoot);
        std::optional<Measure> Take() const;
        void Update(const wchar_t* reason);
        void AfterNextLayout(void (StartMenuHeightHold::*step)());
        void OnVisible();
        void OnLaidOutVisible();
        void Verify();
        void ReassertState();

        winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> m_frameRoot;
        winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> m_panel;
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
        int m_corrections = 0;      // corrections made since the last size or visibility change
        double m_gap = -1;          // the gap Windows leaves above the panel, measured before any change
        double m_height = 0;        // FrameRoot's held Height
        double m_marginTop = 0;     // FrameRoot's held top margin
        double m_originalMax = 0;   // FrameRoot's effective MaxHeight when the hold began (the visual state's, when it owns it)
        winrt::Windows::UI::Xaml::Thickness m_originalMargin{};   // FrameRoot's effective Margin when the hold began
    };
}
