#include "pch.h"
#include "start_menu_width.h"
#include "start_menu_height.h"
#include "../../log.h"
#include "../../util.h"

// What it does mirrors Windhawk's start-menu-size mod, "redesigned" Start menu
// branch (ApplyStyleRedesignedStartMenu: MainMenu's Width, MinWidth and MaxWidth
// set to the wanted width less the room FrameRoot keeps around it), GPL-3.0, by
// m417z and the mod authors:
// https://github.com/ramensoftware/windhawk-mods/blob/main/mods/start-menu-size.wh.cpp
// Different on purpose: the width is held to the room the Start window has, so
// it never overflows a narrow (portrait) screen, and "fill" takes all of it;
// and the three values are held by PropertyOverride, which answers a rewrite
// and gives the original back. See NOTICE.md.

namespace
{
    namespace wf = winrt::Windows::Foundation;
    namespace wux = winrt::Windows::UI::Xaml;
    namespace wuxc = winrt::Windows::UI::Xaml::Controls;
    namespace wuxm = winrt::Windows::UI::Xaml::Media;

    // The narrowest the menu still reads as a menu (Windhawk's floor for the new Start).
    constexpr double kMinimumWidth = 270;
    // Half a DIP: below it a difference is rounding, not a misplacement.
    constexpr double kTolerance = 0.5;

    wux::DependencyObject ParentOf(wux::DependencyObject const& element)
    {
        try { return wuxm::VisualTreeHelper::GetParent(element); }
        catch (...) { return nullptr; }
    }

    wux::FrameworkElement ChildNamed(wux::FrameworkElement const& parent, std::wstring_view name)
    {
        try
        {
            const int count = wuxm::VisualTreeHelper::GetChildrenCount(parent);
            for (int i = 0; i < count; ++i)
            {
                auto child = wuxm::VisualTreeHelper::GetChild(parent, i).try_as<wux::FrameworkElement>();
                if (child && child.Name() == name) return child;
            }
        }
        catch (...) {}
        return nullptr;
    }

    // FrameRoot > AnimationRoot > MainMenu: the panel the person sees.
    wux::FrameworkElement FindMenu(wux::FrameworkElement const& frameRoot)
    {
        auto animationRoot = ChildNamed(frameRoot, L"AnimationRoot");
        return animationRoot ? ChildNamed(animationRoot, L"MainMenu") : nullptr;
    }

    std::wstring Number(double value)
    {
        wchar_t text[32];
        swprintf_s(text, L"%.2f", value);
        return text;
    }
}

namespace deckle::behaviors
{
    double StartMenuWidthHold::Fit(bool fill, double wanted, double room)
    {
        if (room <= 0) return (std::max)(wanted, kMinimumWidth);
        const double width = fill ? room : (std::min)(wanted, room);
        // Not below the floor, unless the screen itself is narrower than it.
        return (std::max)(width, (std::min)(kMinimumWidth, room));
    }

    std::shared_ptr<StartMenuWidthHold> StartMenuWidthHold::TryCreate(
        wux::FrameworkElement const& element, bool fill, double width, std::shared_ptr<rules::OverrideCounters> counters)
    {
        if (!StartMenuHeightHold::IsFrameRoot(element)) return nullptr;
        auto menu = FindMenu(element);
        if (!menu) return nullptr;   // not built yet; FrameRoot is reported again as its content arrives

        std::shared_ptr<StartMenuWidthHold> hold(new StartMenuWidthHold());
        hold->m_counters = std::move(counters);
        hold->m_identity = winrt::get_abi(element.as<wf::IUnknown>());
        hold->m_fill = fill;
        hold->m_wanted = width;
        hold->Attach(element, menu);
        return hold;
    }

    StartMenuWidthHold::~StartMenuWidthHold() = default;

    bool StartMenuWidthHold::IsTargetGone() const
    {
        return !m_frameRoot.get() || !m_menu.get();
    }

    void StartMenuWidthHold::Attach(wux::FrameworkElement const& frameRoot, wux::FrameworkElement const& menu)
    {
        m_frameRoot = winrt::make_weak(frameRoot);
        m_menu = winrt::make_weak(menu);
        m_host = winrt::make_weak(ParentOf(frameRoot).as<wux::FrameworkElement>());
        ++m_counters->matched;

        Log(LogLevel::Info, L"start_menu_width_limits", L"The Start menu width before Deckle holds it",
            { { L"menu_width", Number(menu.Width()) }, { L"menu_min", Number(menu.MinWidth()) }, { L"menu_max", Number(menu.MaxWidth()) },
              { L"menu_actual", Number(menu.ActualWidth()) }, { L"frame_actual", Number(frameRoot.ActualWidth()) },
              { L"frame_halign", std::to_wstring(static_cast<int>(frameRoot.HorizontalAlignment())) } });

        std::weak_ptr<StartMenuWidthHold> weak = weak_from_this();
        // Width alone does not hold. Windows gives MainMenu its width again each
        // time the menu shows (642 or 834 DIP on 26100), in a way that outranks
        // a local Width without rewriting it, so the override is never called
        // back: on 2026-11-10 and 11 the panel read 480 at attach and 834 at
        // every opening. Windows leaves MinWidth and MaxWidth unset, and layout
        // clamps whatever Width says between them.
        for (auto const& property : { wux::FrameworkElement::WidthProperty(),
                                      wux::FrameworkElement::MinWidthProperty(),
                                      wux::FrameworkElement::MaxWidthProperty() })
        {
            m_overrides.push_back(std::make_shared<rules::PropertyOverride>(menu, property,
                [weak]() -> wf::IInspectable
                {
                    auto self = weak.lock();
                    return wf::PropertyValue::CreateDouble(self ? self->m_width : kMinimumWidth);
                },
                m_counters));
        }

        try
        {
            auto window = wux::Window::Current();
            if (window)
            {
                m_window = winrt::make_weak(window);
                m_visibilityToken = window.VisibilityChanged(
                    [weak](wf::IInspectable const&, winrt::Windows::UI::Core::VisibilityChangedEventArgs const& args)
                    {
                        if (!args.Visible()) return;
                        // Not laid out yet at this moment: measure on the next LayoutUpdated.
                        if (auto self = weak.lock()) self->AfterNextLayout(&StartMenuWidthHold::OnLaidOutVisible);
                    });
                m_windowSizeToken = window.SizeChanged(
                    [weak](wf::IInspectable const&, winrt::Windows::UI::Core::WindowSizeChangedEventArgs const&)
                    {
                        if (auto self = weak.lock()) self->Update(L"window_size");
                    });
                if (auto content = window.Content().try_as<wux::FrameworkElement>()) m_content = winrt::make_weak(content);
            }
        }
        catch (winrt::hresult_error const& error)
        {
            Log(LogLevel::Warning, L"start_window_unavailable", L"The Start window could not be watched; the width follows the frame only",
                { { L"hr", util::HandleText(static_cast<uint32_t>(error.code().value)) } });
        }

        if (auto host = m_host.get())
        {
            m_hostSizeToken = host.SizeChanged(
                [weak](wf::IInspectable const&, wux::SizeChangedEventArgs const&)
                {
                    if (auto self = weak.lock()) self->Update(L"frame_size");
                });
        }

        Update(L"attach");
    }

    void StartMenuWidthHold::OnLaidOutVisible()
    {
        Update(L"visible");
    }

    void StartMenuWidthHold::AfterNextLayout(void (StartMenuWidthHold::*step)())
    {
        if (m_layoutPending || m_released) return;
        auto content = m_content.get();
        if (!content)
        {
            (this->*step)();
            return;
        }

        m_layoutPending = true;
        std::weak_ptr<StartMenuWidthHold> weak = weak_from_this();
        m_layoutToken = content.LayoutUpdated(
            [weak, step](wf::IInspectable const&, wf::IInspectable const&)
            {
                auto self = weak.lock();
                if (!self || !self->m_layoutPending) return;
                self->m_layoutPending = false;
                if (auto content = self->m_content.get()) content.LayoutUpdated(self->m_layoutToken);
                self->m_layoutToken = {};
                ((*self).*step)();
            });
    }

    void StartMenuWidthHold::Update(const wchar_t* reason)
    {
        if (m_released) return;
        auto frameRoot = m_frameRoot.get();
        auto menu = m_menu.get();
        auto host = m_host.get();
        if (!frameRoot || !menu || !host) return;

        double window = 0;
        double room = 0;
        wux::Thickness margin{};
        try
        {
            if (auto w = m_window.get()) window = w.Bounds().Width;
            if (window <= 0) window = host.ActualWidth();
            margin = frameRoot.Margin();

            // The room around the panel, once, before the width changes: after,
            // FrameRoot follows the panel and the difference stays the same.
            if (m_room < 0)
            {
                const double frame = frameRoot.ActualWidth();
                const double panel = menu.ActualWidth();
                if (frame <= 0 || panel <= 0) return;   // not laid out yet; a size or visibility event follows
                m_room = (std::max)(frame - panel, 0.0);
            }
            room = window - margin.Left - margin.Right - m_room;
        }
        catch (...)
        {
            return;
        }
        if (window <= 0) return;

        const double width = Fit(m_fill, m_wanted, room);
        const bool changed = !m_applied || width != m_width;
        m_width = width;

        if (!m_applied)
        {
            m_applied = true;
            for (auto const& held : m_overrides) held->Apply();
        }
        else if (changed)
        {
            for (auto const& held : m_overrides) held->Refresh();
        }

        if (changed || reason == std::wstring_view(L"visible"))
        {
            Log(LogLevel::Info, L"start_menu_width", L"The Start menu width follows the window",
                { { L"reason", reason }, { L"mode", m_fill ? L"fill" : L"value" }, { L"wanted", Number(m_wanted) },
                  { L"window", Number(window) }, { L"margin_left", Number(margin.Left) }, { L"margin_right", Number(margin.Right) },
                  { L"shadow_room", Number(m_room) }, { L"room", Number(room) }, { L"width", Number(width) },
                  { L"clamped", !m_fill && width < m_wanted ? L"yes" : L"no" } });
            AfterNextLayout(&StartMenuWidthHold::Verify);
        }
    }

    // Reads the panel back once layout has run: its width, and whether FrameRoot's
    // layout slot is narrower than FrameRoot (which XAML clips).
    void StartMenuWidthHold::Verify()
    {
        if (m_released) return;
        auto frameRoot = m_frameRoot.get();
        auto menu = m_menu.get();
        if (!frameRoot || !menu) return;

        double actual = 0, frame = 0, slot = 0;
        try
        {
            actual = menu.ActualWidth();
            frame = frameRoot.ActualWidth();
            slot = wuxc::Primitives::LayoutInformation::GetLayoutSlot(frameRoot).Width;
        }
        catch (...) { return; }

        const bool off = std::abs(actual - m_width) > kTolerance;
        const bool clipped = slot > 0 && slot + kTolerance < frame;
        Log(off || clipped ? LogLevel::Warning : LogLevel::Info, L"start_menu_width_fit",
            off || clipped ? L"The Start menu is not as wide as intended" : L"The Start menu has the width intended",
            { { L"width", Number(m_width) }, { L"actual", Number(actual) }, { L"frame", Number(frame) },
              { L"slot", Number(slot) }, { L"clipped", clipped ? L"yes" : L"no" } });
    }

    void StartMenuWidthHold::Release()
    {
        if (m_released) return;
        m_released = true;

        try
        {
            if (auto window = m_window.get())
            {
                if (m_visibilityToken.value) window.VisibilityChanged(m_visibilityToken);
                if (m_windowSizeToken.value) window.SizeChanged(m_windowSizeToken);
            }
            if (m_hostSizeToken.value)
            {
                if (auto host = m_host.get()) host.SizeChanged(m_hostSizeToken);
            }
            if (m_layoutToken.value)
            {
                if (auto content = m_content.get()) content.LayoutUpdated(m_layoutToken);
            }
        }
        catch (...) {}
        m_visibilityToken = {};
        m_windowSizeToken = {};
        m_hostSizeToken = {};
        m_layoutToken = {};

        for (auto const& held : m_overrides) held->Restore();
        m_overrides.clear();
    }
}
