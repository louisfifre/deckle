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
// here it is the window's own height in effective pixels, read live, so it holds
// at any scale; FrameRoot's margin stays native and is subtracted instead of
// being zeroed; and the values are held by PropertyOverride, which answers a
// rewrite and gives the original back. See NOTICE.md.

namespace
{
    namespace wf = winrt::Windows::Foundation;
    namespace wux = winrt::Windows::UI::Xaml;
    namespace wuxm = winrt::Windows::UI::Xaml::Media;

    // Larger than any menu content could need; below it the Start menu is a sliver.
    constexpr double kMinimumHeight = 200;

    wux::DependencyObject ParentOf(wux::DependencyObject const& element)
    {
        try { return wuxm::VisualTreeHelper::GetParent(element); }
        catch (...) { return nullptr; }
    }

    std::wstring Number(double value);

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
                        if (path.find(L"Height") == std::wstring::npos && path.find(L"Margin") == std::wstring::npos) continue;
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
              { L"local_height", local(wux::FrameworkElement::HeightProperty()) },
              { L"local_min", local(wux::FrameworkElement::MinHeightProperty()) },
              { L"local_max", local(wux::FrameworkElement::MaxHeightProperty()) },
              { L"start_sizes_state", state }, { L"state_setters", setters } });
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
        m_frameRoot = winrt::make_weak(frameRoot);
        m_host = winrt::make_weak(ParentOf(frameRoot).as<wux::FrameworkElement>());
        ++m_counters->matched;

        // Height, MinHeight and MaxHeight at the same value: the same trio the
        // mod sets. The value is read from m_height each time an override sets.
        std::weak_ptr<StartMenuHeightHold> weak = weak_from_this();
        auto source = [weak]() -> wf::IInspectable
        {
            auto self = weak.lock();
            return wf::PropertyValue::CreateDouble(self ? self->m_height : 0.0);
        };
        for (auto const& property : { wux::FrameworkElement::HeightProperty(),
                                      wux::FrameworkElement::MinHeightProperty(),
                                      wux::FrameworkElement::MaxHeightProperty() })
        {
            m_overrides.push_back(std::make_shared<rules::PropertyOverride>(frameRoot, property, source, m_counters));
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
                        if (auto self = weak.lock()) self->OnVisible();
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
            Log(LogLevel::Warning, L"start_window_unavailable", L"The Start window could not be watched; the height follows the frame only",
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

    void StartMenuHeightHold::OnVisible()
    {
        // The menu has not been laid out yet at this moment: measure on the next LayoutUpdated.
        if (m_layoutPending || m_released) return;
        auto content = m_content.get();
        if (!content)
        {
            Update(L"visible");
            return;
        }

        m_layoutPending = true;
        std::weak_ptr<StartMenuHeightHold> weak = weak_from_this();
        m_layoutToken = content.LayoutUpdated(
            [weak](wf::IInspectable const&, wf::IInspectable const&)
            {
                auto self = weak.lock();
                if (!self || !self->m_layoutPending) return;
                self->m_layoutPending = false;
                if (auto content = self->m_content.get()) content.LayoutUpdated(self->m_layoutToken);
                self->m_layoutToken = {};
                self->Update(L"visible");
            });
    }

    void StartMenuHeightHold::Update(const wchar_t* reason)
    {
        if (m_released) return;
        auto frameRoot = m_frameRoot.get();
        if (!frameRoot) return;

        // The window's size in effective pixels (DIPs): already divided by the
        // scale, so the same code gives 1080 at 100 %, 864 at 125 % on a 1080 screen.
        double window = 0, host = 0;
        try { if (auto w = m_window.get()) window = w.Bounds().Height; } catch (...) {}
        try { if (auto h = m_host.get()) host = h.ActualHeight(); } catch (...) {}
        const double available = window > 0 ? window : host;
        if (available <= 0) return;   // not laid out yet; a size event follows

        // Only for the log: the window's pixels per effective pixel (1.0, 1.25, 1.5 ...).
        double scale = 0;
        try { scale = winrt::Windows::Graphics::Display::DisplayInformation::GetForCurrentView().RawPixelsPerViewPixel(); } catch (...) {}

        wux::Thickness margin = frameRoot.Margin();
        const double height = (std::max)(available - margin.Top - margin.Bottom, kMinimumHeight);

        const bool changed = !m_applied || height != m_height;
        m_height = height;

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
            Log(LogLevel::Info, L"start_menu_height", L"The Start menu height follows the window",
                { { L"reason", reason }, { L"window", Number(window) }, { L"scale", Number(scale) }, { L"frame_slot", Number(host) },
                  { L"margin_top", Number(margin.Top) }, { L"margin_bottom", Number(margin.Bottom) },
                  { L"height", Number(height) }, { L"frame_actual", Number(frameRoot.ActualHeight()) } });
        }
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

    // Gives MaxHeight back to the visual state that owns it. FrameRoot's MaxHeight
    // is set twice: locally (750) and by a StartSizes setter (890), and the setter
    // wins while its state is in force. Writing a local value over it displaces the
    // setter, and giving the local value back does not bring the setter back (the
    // menu then stays capped at 750 instead of 890). A state that is already
    // current is not entered again, so the group is moved to another state and
    // straight back, instantly and with no layout pass between: the setters of the
    // current state apply again over the restored local values.
    void StartMenuHeightHold::ReassertState()
    {
        auto frameRoot = m_frameRoot.get();
        if (!frameRoot) return;

        try
        {
            if (frameRoot.MaxHeight() == m_originalMax) return;   // nothing displaced

            auto group = deckle::rules::FindVisualStateGroup(frameRoot, L"StartSizes");
            auto control = m_host.get().try_as<winrt::Windows::UI::Xaml::Controls::Control>();
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
