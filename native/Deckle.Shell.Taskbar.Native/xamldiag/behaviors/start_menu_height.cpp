#include "pch.h"
#include "start_menu_height.h"
#include "../element_properties.h"
#include "../rules/element_matcher.h"
#include "../../log.h"
#include "../../util.h"

// What it does mirrors Windhawk's start-menu-size mod, "redesigned" Start menu
// branch (ApplyStyleRedesignedStartMenu: FrameRoot Height, MinHeight, MaxHeight
// set to the wanted height; re-applied when Window.VisibilityChanged reports
// visible, on the next LayoutUpdated), GPL-3.0, by m417z and the mod authors:
// https://github.com/ramensoftware/windhawk-mods/blob/main/mods/start-menu-size.wh.cpp
// Different on purpose: the mod's height is a number of pixels from its settings;
// here every figure is measured on the live tree (see the header), so the panel
// fills the window with equal gaps at any screen size and scale; and the values
// are held by PropertyOverride, which answers a rewrite and gives the original
// back. See NOTICE.md.
//
// History (2026-11-09): the first version computed the height from margins read
// on one screen (window - 2 * top margin, the native bottom margin left in
// place). The tree then reported FrameRoot 1080 high, but its layout slot was
// 1044 (the window minus the 36 DIP bottom margin), and XAML clips an element
// whose Height exceeds its slot: the account and power row was cut off on
// screen. Hence the measure, correct, verify loop and the slot check.

namespace
{
    namespace wf = winrt::Windows::Foundation;
    namespace wux = winrt::Windows::UI::Xaml;
    namespace wuxc = winrt::Windows::UI::Xaml::Controls;
    namespace wuxm = winrt::Windows::UI::Xaml::Media;

    // Larger than any menu content could need; below it the Start menu is a sliver.
    constexpr double kMinimumHeight = 200;
    // Half a DIP: below it a difference is rounding, not a misplacement.
    constexpr double kTolerance = 0.5;
    // Corrections after the first placement before the hold settles and logs what remains.
    constexpr int kMaxCorrections = 2;

    wux::DependencyObject ParentOf(wux::DependencyObject const& element)
    {
        try { return wuxm::VisualTreeHelper::GetParent(element); }
        catch (...) { return nullptr; }
    }

    // FrameRoot's child Grid#AnimationRoot: the panel the person sees.
    wux::FrameworkElement FindPanel(wux::FrameworkElement const& frameRoot)
    {
        try
        {
            const int count = wuxm::VisualTreeHelper::GetChildrenCount(frameRoot);
            for (int i = 0; i < count; ++i)
            {
                auto child = wuxm::VisualTreeHelper::GetChild(frameRoot, i).try_as<wux::FrameworkElement>();
                if (child && child.Name() == L"AnimationRoot") return child;
            }
        }
        catch (...) {}
        return nullptr;
    }

    wf::Point Origin(wux::UIElement const& element, wux::UIElement const& relativeTo)
    {
        return element.TransformToVisual(relativeTo).TransformPoint(wf::Point{ 0, 0 });
    }

    std::wstring Number(double value)
    {
        wchar_t text[32];
        swprintf_s(text, L"%.2f", value);
        return text;
    }

    bool SameThickness(wux::Thickness const& a, wux::Thickness const& b)
    {
        return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
    }

    // What limits FrameRoot's height on this build, as the log says it: the values
    // before the behavior touches anything (effective and local), and, for the
    // StartSizes state group, the state in force and its setters on height or
    // margin. Written once per hold so the report of a run carries the facts.
    void LogLimits(wux::FrameworkElement const& frameRoot)
    {
        using deckle::xamldiag::DescribeValue;
        auto local = [&](wux::DependencyProperty const& property)
        {
            try { std::wstring text = DescribeValue(frameRoot.ReadLocalValue(property)); return text.empty() ? std::wstring(L"unset") : text; }
            catch (...) { return std::wstring(L"?"); }
        };

        std::wstring state = L"none", setters;
        try
        {
            if (auto group = deckle::rules::FindVisualStateGroup(frameRoot, L"StartSizes"))
            {
                if (auto current = group.CurrentState())
                {
                    state = current.Name();
                    state += L" (of";
                    for (auto const& other : group.States()) state += L" " + std::wstring(other.Name());
                    state += L")";
                    for (auto const& setterBase : current.Setters())
                    {
                        auto setter = setterBase.try_as<wux::Setter>();
                        if (!setter) continue;
                        std::wstring path;
                        try { path = setter.Target().Path().Path(); } catch (...) { continue; }
                        if (path.find(L"Height") == std::wstring::npos && path.find(L"Margin") == std::wstring::npos
                            && path.find(L"Alignment") == std::wstring::npos) continue;
                        std::wstring owner = L"?";
                        try
                        {
                            if (auto target = setter.Target().Target().try_as<wux::FrameworkElement>())
                                owner = target.Name().empty() ? std::wstring(winrt::get_class_name(target)) : std::wstring(target.Name());
                        }
                        catch (...) {}
                        setters += owner + L"." + path + L"=" + DescribeValue(setter.Value()) + L";";
                    }
                }
            }
        }
        catch (...) {}

        deckle::Log(deckle::LogLevel::Info, L"start_menu_limits", L"What caps the Start menu height before Deckle holds it",
            { { L"effective_height", Number(frameRoot.Height()) }, { L"effective_min", Number(frameRoot.MinHeight()) },
              { L"effective_max", Number(frameRoot.MaxHeight()) }, { L"actual", Number(frameRoot.ActualHeight()) },
              { L"valign", std::to_wstring(static_cast<int>(frameRoot.VerticalAlignment())) },
              { L"local_height", local(wux::FrameworkElement::HeightProperty()) },
              { L"local_min", local(wux::FrameworkElement::MinHeightProperty()) },
              { L"local_max", local(wux::FrameworkElement::MaxHeightProperty()) },
              { L"local_margin", local(wux::FrameworkElement::MarginProperty()) },
              { L"start_sizes_state", state }, { L"state_setters", setters } });
    }
}

namespace deckle::behaviors
{
    bool StartMenuHeightHold::IsFrameRoot(wux::FrameworkElement const& element)
    {
        if (element.Name() != L"FrameRoot") return false;
        if (winrt::get_class_name(element) != L"Windows.UI.Xaml.Controls.Grid") return false;
        auto parent = ParentOf(element);
        return parent && winrt::get_class_name(parent) == L"StartMenu.StartBlendedFlexFrame";
    }

    std::shared_ptr<StartMenuHeightHold> StartMenuHeightHold::TryCreate(
        wux::FrameworkElement const& element, std::shared_ptr<rules::OverrideCounters> counters)
    {
        if (!IsFrameRoot(element)) return nullptr;

        std::shared_ptr<StartMenuHeightHold> hold(new StartMenuHeightHold());
        hold->m_counters = std::move(counters);
        hold->m_identity = winrt::get_abi(element.as<wf::IUnknown>());
        hold->Attach(element);
        return hold;
    }

    StartMenuHeightHold::~StartMenuHeightHold() = default;

    bool StartMenuHeightHold::IsTargetGone() const
    {
        return !m_frameRoot.get();
    }

    void StartMenuHeightHold::Attach(wux::FrameworkElement const& frameRoot)
    {
        LogLimits(frameRoot);
        m_originalMax = frameRoot.MaxHeight();
        m_originalMargin = frameRoot.Margin();
        m_frameRoot = winrt::make_weak(frameRoot);
        m_host = winrt::make_weak(ParentOf(frameRoot).as<wux::FrameworkElement>());
        if (auto panel = FindPanel(frameRoot)) m_panel = winrt::make_weak(panel);
        ++m_counters->matched;

        // Every value is read from the hold's fields each time an override sets.
        std::weak_ptr<StartMenuHeightHold> weak = weak_from_this();
        auto height = [weak]() -> wf::IInspectable
        {
            auto self = weak.lock();
            return wf::PropertyValue::CreateDouble(self ? self->m_height : 0.0);
        };
        for (auto const& property : { wux::FrameworkElement::HeightProperty(),
                                      wux::FrameworkElement::MinHeightProperty(),
                                      wux::FrameworkElement::MaxHeightProperty() })
        {
            m_overrides.push_back(std::make_shared<rules::PropertyOverride>(frameRoot, property, height, m_counters));
        }
        const wux::Thickness native = m_originalMargin;
        m_overrides.push_back(std::make_shared<rules::PropertyOverride>(frameRoot, wux::FrameworkElement::MarginProperty(),
            [weak, native]() -> wf::IInspectable
            {
                auto self = weak.lock();
                return winrt::box_value(wux::Thickness{ native.Left, self ? self->m_marginTop : native.Top, native.Right, 0 });
            },
            m_counters));
        m_overrides.push_back(std::make_shared<rules::PropertyOverride>(frameRoot, wux::FrameworkElement::VerticalAlignmentProperty(),
            []() -> wf::IInspectable { return winrt::box_value(wux::VerticalAlignment::Top); },
            m_counters));

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
                        if (auto self = weak.lock()) self->OnVisible();
                    });
                m_windowSizeToken = window.SizeChanged(
                    [weak](wf::IInspectable const&, winrt::Windows::UI::Core::WindowSizeChangedEventArgs const&)
                    {
                        if (auto self = weak.lock()) { self->m_corrections = 0; self->Update(L"window_size"); }
                    });
                if (auto content = window.Content().try_as<wux::FrameworkElement>()) m_content = winrt::make_weak(content);
            }
        }
        catch (winrt::hresult_error const& error)
        {
            Log(LogLevel::Warning, L"start_window_unavailable", L"The Start window could not be watched; the height follows the frame only",
                { { L"hr", util::HandleText(static_cast<uint32_t>(error.code().value)) } });
        }

        if (auto host = m_host.get())
        {
            m_hostSizeToken = host.SizeChanged(
                [weak](wf::IInspectable const&, wux::SizeChangedEventArgs const&)
                {
                    if (auto self = weak.lock()) { self->m_corrections = 0; self->Update(L"frame_size"); }
                });
        }

        Update(L"attach");
    }

    // The live layout, in DIP relative to the window's content (the host when
    // the window is not reachable). Empty while the menu has not been laid out.
    std::optional<StartMenuHeightHold::Measure> StartMenuHeightHold::Take() const
    {
        auto frameRoot = m_frameRoot.get();
        auto panel = m_panel.get();
        auto host = m_host.get();
        if (!frameRoot || !panel || !host) return std::nullopt;

        try
        {
            wux::UIElement origin = host;
            if (auto content = m_content.get()) origin = content;

            Measure m;
            if (auto window = m_window.get())
            {
                m.window = window.Bounds().Height;
                m.windowWidth = window.Bounds().Width;
            }
            if (m.window <= 0)
            {
                m.window = host.ActualHeight();
                m.windowWidth = host.ActualWidth();
            }
            m.hostTop = Origin(host, origin).Y;
            m.frameTop = Origin(frameRoot, origin).Y;
            m.frameHeight = frameRoot.ActualHeight();
            const auto panelOrigin = Origin(panel, origin);
            m.panelTop = panelOrigin.Y;
            m.panelLeft = panelOrigin.X;
            m.panelHeight = panel.ActualHeight();
            m.panelWidth = panel.ActualWidth();
            if (m.window <= 0 || m.frameHeight <= 0 || m.panelHeight <= 0) return std::nullopt;
            return m;
        }
        catch (...)
        {
            return std::nullopt;
        }
    }

    void StartMenuHeightHold::OnVisible()
    {
        // The menu has not been laid out yet at this moment: measure on the next LayoutUpdated.
        m_corrections = 0;
        AfterNextLayout(&StartMenuHeightHold::OnLaidOutVisible);
    }

    void StartMenuHeightHold::OnLaidOutVisible()
    {
        Update(L"visible");
    }

    void StartMenuHeightHold::AfterNextLayout(void (StartMenuHeightHold::*step)())
    {
        if (m_layoutPending || m_released) return;
        auto content = m_content.get();
        if (!content)
        {
            (this->*step)();
            return;
        }

        m_layoutPending = true;
        std::weak_ptr<StartMenuHeightHold> weak = weak_from_this();
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

    void StartMenuHeightHold::Update(const wchar_t* reason)
    {
        if (m_released) return;
        auto measured = Take();
        if (!measured) return;   // not laid out yet; a size or visibility event follows
        const Measure& m = *measured;

        // The gap is the panel's shadow inset inside FrameRoot: the room Windows
        // keeps around the panel, and the gap it shows on the taskbar's side,
        // where FrameRoot's margin is 0 (12 DIP on 26100, measured on screen).
        // An inset rather than the panel's distance to the window edge: while
        // the menu is closed the panel is moved away for its opening animation,
        // and that distance read -906.
        if (m_gap < 0) m_gap = (std::max)(m.insetTop(), 0.0);

        // FrameRoot is top-aligned in its parent with no bottom margin, so its
        // top is hostTop + marginTop and the panel's is that plus insetTop.
        const double marginTop = m_gap - m.insetTop() - m.hostTop;
        const double height = (std::max)(m.window - 2 * m_gap + m.insetTop() + m.insetBottom(), kMinimumHeight);

        const bool changed = !m_applied || height != m_height || marginTop != m_marginTop;
        m_height = height;
        m_marginTop = marginTop;

        if (!m_applied)
        {
            m_applied = true;
            for (auto& override_ : m_overrides) override_->Apply();
        }
        else
        {
            for (auto& override_ : m_overrides) override_->Refresh();
        }

        if (changed || reason == std::wstring_view(L"visible"))
        {
            double scale = 0;   // only for the log: pixels per DIP (1.0, 1.25, 1.5 ...)
            try { scale = winrt::Windows::Graphics::Display::DisplayInformation::GetForCurrentView().RawPixelsPerViewPixel(); } catch (...) {}
            Log(LogLevel::Info, L"start_menu_height", L"The Start menu height follows the window",
                { { L"reason", reason }, { L"window", Number(m.window) }, { L"scale", Number(scale) }, { L"gap", Number(m_gap) },
                  { L"inset_top", Number(m.insetTop()) }, { L"inset_bottom", Number(m.insetBottom()) }, { L"host_top", Number(m.hostTop) },
                  { L"margin_top", Number(marginTop) }, { L"height", Number(height) } });
        }

        if (changed) AfterNextLayout(&StartMenuHeightHold::Verify);
    }

    // Reads the placement back once layout has run: the gaps above and below the
    // panel, and whether FrameRoot's layout slot is smaller than FrameRoot (which
    // XAML clips). A mismatch gets one more Update, with the insets measured again.
    void StartMenuHeightHold::Verify()
    {
        if (m_released) return;
        auto measured = Take();
        auto frameRoot = m_frameRoot.get();
        if (!measured || !frameRoot) return;
        const Measure& m = *measured;

        double slot = 0;
        try { slot = wuxc::Primitives::LayoutInformation::GetLayoutSlot(frameRoot).Height; } catch (...) {}
        const bool clipped = slot > 0 && slot + kTolerance < m_marginTop + m_height;
        const bool off = std::abs(m.gapTop() - m_gap) > kTolerance || std::abs(m.gapBottom() - m_gap) > kTolerance;

        if ((off || clipped) && m_corrections < kMaxCorrections)
        {
            ++m_corrections;
            Update(L"correction");
            return;
        }

        Log(off || clipped ? LogLevel::Warning : LogLevel::Info, L"start_menu_fit",
            off || clipped ? L"The Start menu does not fit the window as intended" : L"The Start menu fills the window with equal gaps",
            { { L"gap", Number(m_gap) }, { L"gap_top", Number(m.gapTop()) }, { L"gap_bottom", Number(m.gapBottom()) },
              { L"slot", Number(slot) }, { L"wanted", Number(m_marginTop + m_height) }, { L"clipped", clipped ? L"yes" : L"no" },
              { L"corrections", std::to_wstring(m_corrections) } });
    }

    void StartMenuHeightHold::Release()
    {
        if (m_released) return;
        m_released = true;

        try
        {
            if (m_visibilityToken.value || m_windowSizeToken.value)
            {
                if (auto window = m_window.get())
                {
                    if (m_visibilityToken.value) window.VisibilityChanged(m_visibilityToken);
                    if (m_windowSizeToken.value) window.SizeChanged(m_windowSizeToken);
                }
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

        for (auto& override_ : m_overrides) override_->Restore();
        m_overrides.clear();
        ReassertState();
    }

    // Gives back to the visual state what it owns. FrameRoot's MaxHeight is set
    // twice: locally (750) and by a StartSizes setter (890), and the setter wins
    // while its state is in force. Writing a local value over it displaces the
    // setter, and giving the local value back does not bring the setter back (the
    // menu then stays capped at 750 instead of 890). The same holds for any other
    // property a setter owns. A state that is already current is not entered
    // again, so the group is moved to another state and straight back, instantly
    // and with no layout pass between: the setters of the current state apply
    // again over the restored local values.
    void StartMenuHeightHold::ReassertState()
    {
        auto frameRoot = m_frameRoot.get();
        if (!frameRoot) return;

        try
        {
            if (frameRoot.MaxHeight() == m_originalMax && SameThickness(frameRoot.Margin(), m_originalMargin)) return;   // nothing displaced

            auto group = deckle::rules::FindVisualStateGroup(frameRoot, L"StartSizes");
            auto control = m_host.get().try_as<wuxc::Control>();
            auto current = group ? group.CurrentState() : nullptr;
            if (!group || !control || !current)
            {
                Log(LogLevel::Warning, L"start_state_unreachable", L"The Start menu's size state could not be entered again; its MaxHeight stays as restored",
                    { { L"max", Number(frameRoot.MaxHeight()) }, { L"was", Number(m_originalMax) } });
                return;
            }

            const std::wstring home = current.Name().c_str();
            std::wstring away;
            for (auto const& state : group.States())
            {
                if (state.Name() != current.Name()) { away = state.Name().c_str(); break; }
            }
            if (away.empty()) return;

            const double before = frameRoot.MaxHeight();
            wux::VisualStateManager::GoToState(control, away, false);
            wux::VisualStateManager::GoToState(control, home, false);
            Log(LogLevel::Info, L"start_state_reentered", L"Entered the Start menu's size state again so its setters apply",
                { { L"state", home }, { L"via", away }, { L"max_before", Number(before) },
                  { L"max_after", Number(frameRoot.MaxHeight()) }, { L"max_original", Number(m_originalMax) } });
        }
        catch (winrt::hresult_error const& error)
        {
            Log(LogLevel::Warning, L"start_state_failed", L"Entering the Start menu's size state again failed",
                { { L"hr", util::HandleText(static_cast<uint32_t>(error.code().value)) } });
        }
    }
}
