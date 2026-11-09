#include "pch.h"
#include "property_override.h"
#include "../element_properties.h"
#include "../../log.h"
#include "../../util.h"

// The apply / watch / restore cycle follows Windhawk's windows-11-taskbar-styler
// mod (ApplyCustomizationsForVisualStateGroup: ReadLocalValue to remember,
// SetValue to apply, RegisterPropertyChangedCallback to hold the value against
// Explorer's code, a re-entrancy guard), GPL-3.0. See NOTICE.md.

namespace
{
    namespace wf = winrt::Windows::Foundation;
    namespace wux = winrt::Windows::UI::Xaml;

    std::wstring ErrorText(winrt::hresult_error const& error, wf::IInspectable const& value = nullptr)
    {
        std::wstring text = deckle::util::HandleText(static_cast<uint32_t>(error.code().value)) + L" " + std::wstring(error.message());
        // The class of the value the property refused tells a wrong type from a wrong value.
        if (value) text += L" (value: " + std::wstring(winrt::get_class_name(value)) + L")";
        return text;
    }
}

namespace deckle::rules
{
    void OverrideCounters::SetError(std::wstring text)
    {
        std::lock_guard guard(m_lock);
        m_error = std::move(text);
    }

    std::wstring OverrideCounters::Error() const
    {
        std::lock_guard guard(m_lock);
        return m_error;
    }

    bool SameValue(wf::IInspectable const& a, wf::IInspectable const& b)
    {
        if (a == b) return true;
        if (!a || !b) return false;
        // An unset value has no text; it is only the same as itself.
        if (a == wux::DependencyProperty::UnsetValue() || b == wux::DependencyProperty::UnsetValue()) return false;
        return winrt::get_class_name(a) == winrt::get_class_name(b)
            && xamldiag::DescribeValue(a) == xamldiag::DescribeValue(b);
    }

    PropertyOverride::PropertyOverride(wux::DependencyObject const& target, wux::DependencyProperty const& property,
                                       ValueSource source, std::shared_ptr<OverrideCounters> counters, bool followForeignWrites)
        : m_target(winrt::make_weak(target)), m_property(property), m_source(std::move(source)), m_counters(std::move(counters)),
          m_followForeignWrites(followForeignWrites)
    {
    }

    PropertyOverride::~PropertyOverride()
    {
        // An override still active here lost its element (XAML dropped it).
        if (m_active) --m_counters->active;
    }

    std::wstring PropertyOverride::OriginalText() const
    {
        if (!m_original || m_original == wux::DependencyProperty::UnsetValue()) return L"unset";
        std::wstring text = xamldiag::DescribeValue(m_original);
        return text.empty() ? std::wstring(winrt::get_class_name(m_original)) : text;
    }

    bool PropertyOverride::IsTargetGone() const
    {
        return !m_target.get();
    }

    bool PropertyOverride::Apply()
    {
        auto target = m_target.get();
        if (!target) return false;

        try
        {
            m_original = target.ReadLocalValue(m_property);

            std::weak_ptr<PropertyOverride> weak = weak_from_this();
            m_token = target.RegisterPropertyChangedCallback(m_property,
                [weak](wux::DependencyObject const&, wux::DependencyProperty const&)
                {
                    if (auto self = weak.lock()) self->OnChanged();
                });

            m_active = true;
            ++m_counters->active;
            Set(target);
            return true;
        }
        catch (winrt::hresult_error const& error)
        {
            ++m_counters->failed;
            if (m_counters->Error().empty()) m_counters->SetError(ErrorText(error));
            if (m_active)
            {
                m_active = false;
                --m_counters->active;
            }
            if (m_token != 0)
            {
                try { target.UnregisterPropertyChangedCallback(m_property, m_token); } catch (...) {}
                m_token = 0;
            }
            m_original = nullptr;
            return false;
        }
    }

    void PropertyOverride::Set(wux::DependencyObject const& target)
    {
        wf::IInspectable value = m_source();
        m_setting = true;
        try
        {
            target.SetValue(m_property, value);
        }
        catch (winrt::hresult_error const& error)
        {
            m_setting = false;
            m_counters->SetError(ErrorText(error, value));
            throw;
        }
        catch (...)
        {
            m_setting = false;
            throw;
        }
        m_setting = false;
        m_ours = value;
    }

    void PropertyOverride::Refresh()
    {
        if (!m_active) return;
        auto target = m_target.get();
        if (!target) return;

        try
        {
            if (!SameValue(target.ReadLocalValue(m_property), m_source())) Set(target);
        }
        catch (winrt::hresult_error const& error)
        {
            ++m_counters->failed;
            m_counters->SetError(ErrorText(error));
        }
    }

    void PropertyOverride::OnChanged()
    {
        if (m_setting || !m_active) return;
        auto target = m_target.get();
        if (!target) return;

        try
        {
            wf::IInspectable current = target.ReadLocalValue(m_property);
            if (SameValue(current, m_ours)) return;

            // Explorer's code (or a binding, or a state change) wrote the property.
            if (m_followForeignWrites) m_original = current;

            const auto now = std::chrono::steady_clock::now();
            if (now - m_windowStart > std::chrono::seconds(1))
            {
                m_windowStart = now;
                m_windowCount = 0;
                m_windowContested = false;
            }
            if (++m_windowCount > kMaxReassertsPerSecond)
            {
                if (!m_windowContested)
                {
                    m_windowContested = true;
                    ++m_counters->contested;
                }
                return;
            }

            ++m_counters->reasserted;
            Set(target);
        }
        catch (winrt::hresult_error const& error)
        {
            ++m_counters->failed;
            m_counters->SetError(ErrorText(error));
        }
    }

    void PropertyOverride::Restore()
    {
        if (!m_active) return;
        m_active = false;
        --m_counters->active;

        auto target = m_target.get();
        wf::IInspectable original = std::move(m_original);
        m_original = nullptr;
        m_ours = nullptr;
        if (!target) return;

        // Stop watching first: the restoring write must not be answered.
        try { target.UnregisterPropertyChangedCallback(m_property, m_token); } catch (...) {}
        m_token = 0;

        std::wstring giving = !original || original == wux::DependencyProperty::UnsetValue() ? std::wstring(L"unset") : xamldiag::DescribeValue(original);
        try
        {
            if (!original || original == wux::DependencyProperty::UnsetValue())
            {
                target.ClearValue(m_property);
            }
            else
            {
                target.SetValue(m_property, original);
            }
            Log(LogLevel::Verbose, L"override_restored", L"An override gave a property back",
                { { L"type", std::wstring(winrt::get_class_name(target)) }, { L"gave", giving },
                  { L"reads", xamldiag::DescribeValue(target.ReadLocalValue(m_property)) } });
        }
        catch (...)
        {
            // A local value the property cannot take back (a binding
            // expression, say): the style's value is the next best.
            try { target.ClearValue(m_property); } catch (...) {}
        }
    }
}
