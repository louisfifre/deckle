#pragma once

#include <chrono>

namespace deckle::rules
{
    // What a rule (or a behavior) reports about the overrides it owns. Plain
    // numbers, read by the status reporter from another thread.
    class OverrideCounters
    {
    public:
        std::atomic<uint32_t> matched{ 0 };      // elements the rule found
        std::atomic<uint32_t> active{ 0 };       // overrides in force now
        std::atomic<uint32_t> reasserted{ 0 };   // times Explorer's code changed the value and the engine set it back
        std::atomic<uint32_t> contested{ 0 };    // times the re-assert budget ran out: Explorer's code keeps winning
        std::atomic<uint32_t> failed{ 0 };       // overrides that could not be applied

        void SetError(std::wstring text);
        std::wstring Error() const;

    private:
        mutable std::mutex m_lock;
        std::wstring m_error;
    };

    // One property of one element, held at a value Deckle chose.
    //
    //   Apply    remembers the value the property had (ReadLocalValue), sets
    //            Deckle's, and watches the property.
    //   watch    Explorer's code rewrites many of these values (it resets the
    //            height of a tray icon each time the layout runs). The watch is
    //            RegisterPropertyChangedCallback; when the value stops being
    //            Deckle's, the engine sets it back.
    //   give back Restore returns the value the property had when Apply began,
    //            which is the state Explorer built for itself. A value that
    //            Explorer rewrites on purpose and keeps current, the clock's
    //            text, is the exception (followForeignWrites): the last value
    //            Explorer wrote is the one to give back. Following every write
    //            did harm on the clock's TextAlignment, which Explorer sets
    //            to different values while it lays out, so a rule restores the first.
    //   budget   A rewrite that keeps winning would loop. More than
    //            kMaxReassertsPerSecond in a second is not answered until the
    //            second is over, and counts as contested.
    //   Restore  stops watching, then gives the remembered value back (or clears
    //            the local value when the property had none).
    //
    // Everything here runs on the UI thread of the element, which is the only
    // thread that may touch it. The element is held weakly: when XAML drops it,
    // the override ends with nothing to undo.
    class PropertyOverride : public std::enable_shared_from_this<PropertyOverride>
    {
    public:
        // The value to hold now. Called on the UI thread each time the engine
        // sets the property; a constant for a rule, the current time for the clock.
        using ValueSource = std::function<winrt::Windows::Foundation::IInspectable()>;

        static constexpr uint32_t kMaxReassertsPerSecond = 20;

        PropertyOverride(winrt::Windows::UI::Xaml::DependencyObject const& target,
                         winrt::Windows::UI::Xaml::DependencyProperty const& property,
                         ValueSource source,
                         std::shared_ptr<OverrideCounters> counters,
                         bool followForeignWrites = false);
        PropertyOverride(const PropertyOverride&) = delete;
        PropertyOverride& operator=(const PropertyOverride&) = delete;
        ~PropertyOverride();

        // False when the value could not be set; the error is in the counters.
        bool Apply();

        // Sets the value again if it is not the source's current one.
        void Refresh();

        void Restore();

        bool IsActive() const { return m_active; }

        // What the property held before Apply, as text, for the log: "unset" when
        // it had no local value.
        std::wstring OriginalText() const;

        // True when the target no longer exists. UI thread only.
        bool IsTargetGone() const;

    private:
        void OnChanged();
        void Set(winrt::Windows::UI::Xaml::DependencyObject const& target);

        winrt::weak_ref<winrt::Windows::UI::Xaml::DependencyObject> m_target;
        winrt::Windows::UI::Xaml::DependencyProperty m_property;
        ValueSource m_source;
        std::shared_ptr<OverrideCounters> m_counters;
        bool m_followForeignWrites;

        bool m_active = false;
        bool m_setting = false;   // true while the engine itself writes the property
        int64_t m_token = 0;
        winrt::Windows::Foundation::IInspectable m_original{ nullptr };
        winrt::Windows::Foundation::IInspectable m_ours{ nullptr };
        std::chrono::steady_clock::time_point m_windowStart{};
        uint32_t m_windowCount = 0;
        bool m_windowContested = false;
    };

    // Whether two property values are the same value. Objects are compared by
    // identity first, then by what they say (DescribeValue): a boxed number is a
    // new object on every read.
    bool SameValue(winrt::Windows::Foundation::IInspectable const& a, winrt::Windows::Foundation::IInspectable const& b);
}
