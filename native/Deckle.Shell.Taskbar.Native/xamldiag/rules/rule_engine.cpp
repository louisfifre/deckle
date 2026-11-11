#include "pch.h"
#include "rule_engine.h"
#include "element_matcher.h"
#include "property_override.h"
#include "selector.h"
#include "style_resolver.h"
#include "../behaviors/clock.h"
#include "../behaviors/start_menu_height.h"
#include "../behaviors/start_menu_width.h"
#include "../element_properties.h"
#include "../../channel.h"
#include "../../log.h"
#include "../../util.h"

// The rule model, the per-type value conversion and the apply / re-assert /
// restore cycle follow Windhawk's windows-11-taskbar-styler mod, GPL-3.0, by
// m417z and the mod authors:
// https://github.com/ramensoftware/windhawk-mods/blob/main/mods/windows-11-taskbar-styler.wh.cpp
// See NOTICE.md.

namespace
{
    namespace wf = winrt::Windows::Foundation;
    namespace wux = winrt::Windows::UI::Xaml;
    namespace wuxc = winrt::Windows::UI::Xaml::Controls;
    namespace wuxm = winrt::Windows::UI::Xaml::Media;
    namespace wuc = winrt::Windows::UI::Core;
    namespace json = winrt::Windows::Data::Json;
    using namespace deckle::rules;
    using namespace std::chrono_literals;
    using Clock = std::chrono::steady_clock;

    constexpr auto kWaitForUiThread = 3000ms;
    constexpr auto kSettleTime = 10s;

    // One rule of the current document, ready to match.
    struct RuleRuntime
    {
        RuleDefinition definition;
        Selector selector;
        std::wstring problem;   // why the rule can never apply; empty when it can
        std::shared_ptr<OverrideCounters> counters = std::make_shared<OverrideCounters>();
    };

    struct RuleSet
    {
        uint64_t generation = 0;
        Clock::time_point appliedAt;
        std::vector<std::shared_ptr<RuleRuntime>> rules;
        std::shared_ptr<const ClockConfiguration> clock;
        std::shared_ptr<OverrideCounters> clockCounters = std::make_shared<OverrideCounters>();
        bool startMenuHeight = false;
        std::shared_ptr<OverrideCounters> startMenuCounters = std::make_shared<OverrideCounters>();
        StartMenuWidthConfiguration startMenuWidth;
        std::shared_ptr<OverrideCounters> startMenuWidthCounters = std::make_shared<OverrideCounters>();
        std::vector<std::wstring> problems;
    };

    // What the XAML parser made of a rule's property and value, on one UI thread.
    struct Resolved
    {
        wux::DependencyProperty property{ nullptr };
        wf::IInspectable value{ nullptr };
        bool shareable = true;
    };

    // One override in force (or waiting for its visual state).
    struct Held
    {
        void* target = nullptr;     // the element's identity
        void* property = nullptr;   // the DependencyProperty's identity
        std::shared_ptr<PropertyOverride> override_;
        wux::VisualStateGroup group{ nullptr };   // the gate of a rule with a visualState
        winrt::event_token stateToken{};
    };

    struct Tracked
    {
        void* key = nullptr;
        winrt::weak_ref<wux::FrameworkElement> element;
    };

    // Everything the engine keeps about one UI thread. The lists belong to that
    // thread: only functions running on it may read or change them.
    struct ThreadContext
    {
        DWORD tid = 0;
        wuc::CoreDispatcher dispatcher{ nullptr };
        std::vector<Tracked> tracked;
        std::vector<Held> held;
        std::vector<std::shared_ptr<deckle::behaviors::StartMenuHeightHold>> startHolds;
        std::vector<std::shared_ptr<deckle::behaviors::StartMenuWidthHold>> widthHolds;
        std::map<std::pair<uint64_t, size_t>, Resolved> resolved;
    };

    void* Identity(wf::IInspectable const& object)
    {
        return winrt::get_abi(object.as<wf::IUnknown>());
    }

    std::wstring Hex(winrt::hresult_error const& error)
    {
        return deckle::util::HandleText(static_cast<uint32_t>(error.code().value)) + L" " + std::wstring(error.message());
    }

    void Teardown(Held& held)
    {
        if (held.group)
        {
            try { held.group.CurrentStateChanged(held.stateToken); } catch (...) {}
            held.group = nullptr;
        }
        if (held.override_) held.override_->Restore();
    }

    void Post(json::JsonObject const& object)
    {
        deckle::Channel::Instance().Post(deckle::util::ToUtf8(object.Stringify()));
    }

    json::JsonValue Text(std::wstring_view value)
    {
        return json::JsonValue::CreateStringValue(winrt::hstring(value));
    }

    wux::DependencyObject ParentOf(wux::DependencyObject const& element)
    {
        try { return wuxm::VisualTreeHelper::GetParent(element); }
        catch (...) { return nullptr; }
    }

    void DumpElement(std::wstring& out, wux::DependencyObject const& element, int depth)
    {
        out.append(static_cast<size_t>(depth) * 2, L' ');
        out += winrt::get_class_name(element);
        if (auto framework = element.try_as<wux::FrameworkElement>())
        {
            if (!framework.Name().empty()) out += L"#" + std::wstring(framework.Name());
        }
        std::wstring props = deckle::xamldiag::DescribeElement(element);
        if (!props.empty()) out += L"  [" + props + L"]";
        out += L'\n';

        int count = 0;
        try { count = wuxm::VisualTreeHelper::GetChildrenCount(element); } catch (...) {}
        for (int i = 0; i < count; ++i)
        {
            try { DumpElement(out, wuxm::VisualTreeHelper::GetChild(element, i), depth + 1); } catch (...) {}
        }
    }
}

namespace deckle::rules
{
    struct Engine::Impl
    {
        std::mutex lock;   // the contexts map and `current`
        std::map<DWORD, std::unique_ptr<ThreadContext>> contexts;
        std::shared_ptr<const RuleSet> current;
        uint64_t generation = 0;

        std::atomic<bool> shuttingDown{ false };
        HANDLE tickerStop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        std::thread ticker;

        // Touched by the ticker and by Apply, under statusLock.
        std::mutex statusLock;
        std::wstring lastStatus;
        Clock::time_point lastStatusAt{};
        int64_t lastMinute = 0;
        Clock::time_point lastRefresh{};
        Clock::time_point lastPrune{};

        std::shared_ptr<const RuleSet> Current()
        {
            std::lock_guard guard(lock);
            return current;
        }

        ThreadContext& ContextForCurrentThread(wux::FrameworkElement const& element)
        {
            const DWORD tid = GetCurrentThreadId();
            std::lock_guard guard(lock);
            auto& slot = contexts[tid];
            if (!slot)
            {
                slot = std::make_unique<ThreadContext>();
                slot->tid = tid;
                try
                {
                    slot->dispatcher = element.Dispatcher();
                }
                catch (winrt::hresult_error const& error)
                {
                    Log(LogLevel::Warning, L"dispatcher_missing", L"A XAML thread has no dispatcher, so the engine cannot reach it",
                        { { L"tid", std::to_wstring(tid) }, { L"hr", Hex(error) } });
                }
                Log(LogLevel::Info, L"ui_thread", L"The engine met a XAML thread",
                    { { L"tid", std::to_wstring(tid) }, { L"dispatcher", slot->dispatcher ? L"yes" : L"no" } });
            }
            return *slot;
        }

        std::vector<ThreadContext*> Contexts()
        {
            std::lock_guard guard(lock);
            std::vector<ThreadContext*> result;
            for (auto& [tid, context] : contexts) result.push_back(context.get());
            return result;
        }

        // Runs `work` on the context's thread. With a wait, returns whether it
        // finished in time; without, whether it was queued. A function that
        // outlives the wait still runs later, so it must only hold shared state.
        static bool RunOn(ThreadContext& context, std::function<void()> work, std::chrono::milliseconds wait)
        {
            if (GetCurrentThreadId() == context.tid)
            {
                work();
                return true;
            }

            wuc::CoreDispatcher dispatcher = context.dispatcher;
            if (!dispatcher) return false;

            struct Signal
            {
                HANDLE event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
                ~Signal() { CloseHandle(event); }
            };
            auto signal = std::make_shared<Signal>();

            try
            {
                dispatcher.RunAsync(wuc::CoreDispatcherPriority::Normal, [work = std::move(work), signal]
                {
                    try { work(); } catch (...) {}
                    SetEvent(signal->event);
                });
            }
            catch (...)
            {
                return false;
            }

            if (wait.count() == 0) return true;
            return WaitForSingleObject(signal->event, static_cast<DWORD>(wait.count())) == WAIT_OBJECT_0;
        }

        // ── On a UI thread ────────────────────────────────────────────────

        static void Track(ThreadContext& context, wux::FrameworkElement const& element)
        {
            void* key = Identity(element);
            for (Tracked& entry : context.tracked)
            {
                if (entry.key != key) continue;
                if (!entry.element.get()) entry.element = winrt::make_weak(element);
                return;
            }
            context.tracked.push_back({ key, winrt::make_weak(element) });
        }

        static Held* Find(ThreadContext& context, void* target, void* property)
        {
            for (Held& held : context.held)
            {
                if (held.target == target && held.property == property) return &held;
            }
            return nullptr;
        }

        static Resolved& Resolve(ThreadContext& context, const RuleSet& set, size_t index)
        {
            const auto key = std::make_pair(set.generation, index);
            if (auto found = context.resolved.find(key); found != context.resolved.end()) return found->second;

            const RuleRuntime& rule = *set.rules[index];
            Resolved resolved;
            try
            {
                std::wstring type = rule.selector.path.back().typeName;
                if (type.empty()) type = L"Windows.UI.Xaml.FrameworkElement";
                ResolvedSetter setter = ResolveSetter(type, rule.definition.property, rule.definition.value);
                resolved.property = setter.property;
                resolved.value = setter.value;
                resolved.shareable = IsShareable(setter.value);
            }
            catch (winrt::hresult_error const& error)
            {
                ++rule.counters->failed;
                rule.counters->SetError(Hex(error));
                Log(LogLevel::Warning, L"rule_unresolved", L"A rule's property or value could not be read",
                    { { L"rule", rule.definition.id }, { L"error", Hex(error) } });
            }
            return context.resolved.emplace(key, std::move(resolved)).first->second;
        }

        static void ApplyRule(ThreadContext& context, wux::FrameworkElement const& element, const RuleSet& set, size_t index)
        {
            const RuleRuntime& rule = *set.rules[index];
            Resolved& resolved = Resolve(context, set, index);
            if (!resolved.property) return;

            void* target = Identity(element);
            void* property = winrt::get_abi(resolved.property);
            if (Find(context, target, property)) return;   // an earlier rule, or this one on an earlier report, holds it

            wf::IInspectable value = resolved.value;
            if (!resolved.shareable)
            {
                // A Transform, say, has one parent: each element gets its own.
                try
                {
                    std::wstring type = rule.selector.path.back().typeName;
                    if (type.empty()) type = L"Windows.UI.Xaml.FrameworkElement";
                    value = ResolveSetter(type, rule.definition.property, rule.definition.value).value;
                }
                catch (winrt::hresult_error const& error)
                {
                    ++rule.counters->failed;
                    rule.counters->SetError(Hex(error));
                    return;
                }
            }

            ++rule.counters->matched;

            Held held;
            held.target = target;
            held.property = property;
            held.override_ = std::make_shared<PropertyOverride>(
                element, resolved.property, [value] { return value; }, rule.counters);

            if (rule.definition.visualState.empty())
            {
                if (held.override_->Apply())
                {
                    Log(LogLevel::Verbose, L"rule_applied", L"A rule took hold of an element",
                        { { L"rule", rule.definition.id }, { L"type", std::wstring(winrt::get_class_name(element)) },
                          { L"was", held.override_->OriginalText() }, { L"tid", std::to_wstring(GetCurrentThreadId()) } });
                }
            }
            else if (auto group = FindVisualStateGroup(element, rule.selector.path.back().visualStateGroup))
            {
                // The override waits for the state: it applies when the group
                // enters it and is given back when the group leaves.
                std::weak_ptr<PropertyOverride> weak = held.override_;
                std::wstring wanted = rule.definition.visualState;
                held.group = group;
                held.stateToken = group.CurrentStateChanged(
                    [weak, wanted](wf::IInspectable const&, wux::VisualStateChangedEventArgs const& args)
                    {
                        auto override_ = weak.lock();
                        if (!override_) return;
                        auto state = args.NewState();
                        if (state && state.Name() == wanted)
                        {
                            if (!override_->IsActive()) override_->Apply();
                        }
                        else
                        {
                            override_->Restore();
                        }
                    });

                auto state = group.CurrentState();
                if (state && state.Name() == wanted) held.override_->Apply();
            }

            context.held.push_back(std::move(held));
        }

        static void ApplyClock(ThreadContext& context, wux::FrameworkElement const& element, const RuleSet& set)
        {
            if (!set.clock || !set.clock->enabled) return;
            auto block = behaviors::ClassifyClockBlock(element);
            if (block == behaviors::ClockBlock::None) return;

            auto property = wuxc::TextBlock::TextProperty();
            void* target = Identity(element);
            if (Find(context, target, winrt::get_abi(property))) return;

            ++set.clockCounters->matched;

            auto configuration = set.clock;
            Held held;
            held.target = target;
            held.property = winrt::get_abi(property);
            held.override_ = std::make_shared<PropertyOverride>(
                element, property,
                [configuration, block]
                {
                    return wf::PropertyValue::CreateString(winrt::hstring(behaviors::FormatClockBlock(*configuration, block)));
                },
                set.clockCounters,
                true);   // Explorer writes the text each minute: the last write is the one to give back
            if (held.override_->Apply())
            {
                Log(LogLevel::Info, L"clock_block_applied", L"The clock text is now written by Deckle",
                    { { L"block", block == behaviors::ClockBlock::Upper ? L"upper" : L"lower" },
                      { L"tid", std::to_wstring(GetCurrentThreadId()) } });
            }
            context.held.push_back(std::move(held));
        }

        static void ApplyStartMenuHeight(ThreadContext& context, wux::FrameworkElement const& element, const RuleSet& set)
        {
            if (!set.startMenuHeight) return;
            if (!behaviors::StartMenuHeightHold::IsFrameRoot(element)) return;

            void* target = Identity(element);
            for (auto const& hold : context.startHolds)
            {
                if (hold->TargetIdentity() == target) return;
            }

            try
            {
                if (auto hold = behaviors::StartMenuHeightHold::TryCreate(element, set.startMenuCounters))
                {
                    Log(LogLevel::Info, L"start_menu_hold", L"The Start menu height is now held by Deckle",
                        { { L"tid", std::to_wstring(GetCurrentThreadId()) } });
                    context.startHolds.push_back(std::move(hold));
                }
            }
            catch (winrt::hresult_error const& error)
            {
                ++set.startMenuCounters->failed;
                set.startMenuCounters->SetError(Hex(error));
            }
        }

        static void ApplyStartMenuWidth(ThreadContext& context, wux::FrameworkElement const& element, const RuleSet& set)
        {
            if (!set.startMenuWidth.enabled) return;

            // The tree reports FrameRoot before its content, so the hold is also
            // tried when MainMenu (FrameRoot > AnimationRoot > MainMenu) arrives.
            wux::FrameworkElement frameRoot = element;
            if (element.Name() == L"MainMenu")
            {
                auto animationRoot = ParentOf(element);
                frameRoot = animationRoot ? ParentOf(animationRoot).try_as<wux::FrameworkElement>() : nullptr;
                if (!frameRoot) return;
            }
            if (!behaviors::StartMenuHeightHold::IsFrameRoot(frameRoot)) return;

            void* target = Identity(frameRoot);
            for (auto const& hold : context.widthHolds)
            {
                if (hold->TargetIdentity() == target) return;
            }

            try
            {
                if (auto hold = behaviors::StartMenuWidthHold::TryCreate(
                        frameRoot, set.startMenuWidth.fill, set.startMenuWidth.width, set.startMenuWidthCounters))
                {
                    Log(LogLevel::Info, L"start_menu_width_hold", L"The Start menu width is now held by Deckle",
                        { { L"tid", std::to_wstring(GetCurrentThreadId()) } });
                    context.widthHolds.push_back(std::move(hold));
                }
            }
            catch (winrt::hresult_error const& error)
            {
                ++set.startMenuWidthCounters->failed;
                set.startMenuWidthCounters->SetError(Hex(error));
            }
        }

        static void ApplyRules(ThreadContext& context, wux::FrameworkElement const& element, const RuleSet& set)
        {
            for (size_t i = 0; i < set.rules.size(); ++i)
            {
                const RuleRuntime& rule = *set.rules[i];
                if (!rule.problem.empty()) continue;
                if (!MatchesSelector(element, rule.selector)) continue;
                ApplyRule(context, element, set, i);
            }
            ApplyClock(context, element, set);
            ApplyStartMenuHeight(context, element, set);
            ApplyStartMenuWidth(context, element, set);
        }

        static size_t ClearAll(ThreadContext& context)
        {
            size_t active = 0;
            for (Held& held : context.held)
            {
                if (held.override_ && held.override_->IsActive()) ++active;
                Teardown(held);
            }
            context.held.clear();
            for (auto& hold : context.startHolds)
            {
                ++active;
                hold->Release();
            }
            context.startHolds.clear();
            for (auto& hold : context.widthHolds)
            {
                ++active;
                hold->Release();
            }
            context.widthHolds.clear();
            context.resolved.clear();
            return active;
        }

        static void Prune(ThreadContext& context)
        {
            std::erase_if(context.tracked, [](Tracked const& entry) { return !entry.element.get(); });
            std::erase_if(context.held, [](Held const& held) { return held.override_->IsTargetGone(); });
            std::erase_if(context.startHolds, [](auto const& hold) { return hold->IsTargetGone(); });
            std::erase_if(context.widthHolds, [](auto const& hold) { return hold->IsTargetGone(); });
        }

        static void Reapply(ThreadContext& context, const std::shared_ptr<const RuleSet>& set)
        {
            ClearAll(context);
            Prune(context);
            for (Tracked const& entry : context.tracked)
            {
                if (auto element = entry.element.get()) ApplyRules(context, element, *set);
            }
        }

        static void RefreshClocks(ThreadContext& context)
        {
            for (Held& held : context.held)
            {
                if (held.override_ && held.override_->IsActive() && !held.group) held.override_->Refresh();
            }
        }

        // ── Status ────────────────────────────────────────────────────────

        static const wchar_t* StateOf(const std::wstring& problem, OverrideCounters& counters, bool settled)
        {
            if (!problem.empty()) return L"error";
            const uint32_t active = counters.active;
            if (active == 0 && !counters.Error().empty()) return L"error";
            if (counters.contested > 0) return L"contested";
            if (active > 0) return L"applied";
            if (counters.matched == 0) return settled ? L"never_matched" : L"pending";
            return L"idle";   // matched, but the element is gone or the visual state is off
        }

        static json::JsonObject RuleStatus(std::wstring_view id, const std::wstring& problem, OverrideCounters& counters, bool settled)
        {
            json::JsonObject entry;
            entry.SetNamedValue(L"id", Text(id));
            entry.SetNamedValue(L"state", Text(StateOf(problem, counters, settled)));
            entry.SetNamedValue(L"matched", Text(std::to_wstring(counters.matched.load())));
            entry.SetNamedValue(L"active", Text(std::to_wstring(counters.active.load())));
            entry.SetNamedValue(L"reasserted", Text(std::to_wstring(counters.reasserted.load())));
            entry.SetNamedValue(L"contested", Text(std::to_wstring(counters.contested.load())));
            std::wstring error = problem.empty() ? counters.Error() : problem;
            entry.SetNamedValue(L"error", Text(error));
            return entry;
        }

        void SendStatus(bool force)
        {
            auto set = Current();
            if (!set) return;

            const bool settled = Clock::now() - set->appliedAt >= kSettleTime;

            json::JsonObject status;
            status.SetNamedValue(L"t", Text(L"status"));
            status.SetNamedValue(L"generation", Text(std::to_wstring(set->generation)));
            status.SetNamedValue(L"settled", Text(settled ? L"true" : L"false"));

            json::JsonArray rules;
            for (auto const& rule : set->rules)
            {
                rules.Append(RuleStatus(rule->definition.id, rule->problem, *rule->counters, settled));
            }
            status.SetNamedValue(L"rules", rules);

            json::JsonArray behaviors;
            if (set->clock && set->clock->enabled)
            {
                behaviors.Append(RuleStatus(L"clock", L"", *set->clockCounters, settled));
            }
            if (set->startMenuHeight)
            {
                behaviors.Append(RuleStatus(L"startMenuHeight", L"", *set->startMenuCounters, settled));
            }
            if (set->startMenuWidth.enabled)
            {
                behaviors.Append(RuleStatus(L"startMenuWidth", L"", *set->startMenuWidthCounters, settled));
            }
            status.SetNamedValue(L"behaviors", behaviors);

            json::JsonArray problems;
            for (auto const& problem : set->problems) problems.Append(Text(problem));
            status.SetNamedValue(L"problems", problems);

            std::wstring text(status.Stringify());
            std::lock_guard guard(statusLock);
            const auto now = Clock::now();
            if (!force && (text == lastStatus || now - lastStatusAt < 1s)) return;
            lastStatus = text;
            lastStatusAt = now;
            deckle::Channel::Instance().Post(deckle::util::ToUtf8(text));
        }

        // ── The timer thread ──────────────────────────────────────────────

        void TickerMain()
        {
            while (WaitForSingleObject(tickerStop, 500) == WAIT_TIMEOUT)
            {
                try
                {
                    const auto now = Clock::now();

                    // Parked too: the watcher keeps reporting elements while no
                    // document is in force, and the dead ones must still go.
                    if (now - lastPrune >= 5s)
                    {
                        lastPrune = now;
                        for (ThreadContext* context : Contexts())
                        {
                            RunOn(*context, [context] { Prune(*context); }, 0ms);
                        }
                    }

                    auto set = Current();
                    if (!set) continue;

                    // The clock: rewrite the text when the minute changes, and
                    // every 30 s regardless, in case a write of Explorer's
                    // slipped past the property callback.
                    if (set->clock && set->clock->enabled)
                    {
                        const int64_t minute = behaviors::CurrentMinute();
                        if (minute != lastMinute || now - lastRefresh >= 30s)
                        {
                            lastMinute = minute;
                            lastRefresh = now;
                            for (ThreadContext* context : Contexts())
                            {
                                RunOn(*context, [context] { RefreshClocks(*context); }, 0ms);
                            }
                        }
                    }

                    SendStatus(false);
                }
                catch (...) {}
            }
        }
    };

    Engine::Engine() : m_impl(new Impl()) {}

    Engine& Engine::Instance()
    {
        // Leaked on purpose, like the channel: a static destroyed at explorer's
        // exit could run after this DLL is gone.
        static Engine* instance = new Engine();
        return *instance;
    }

    void Engine::OnElementAdded(wux::FrameworkElement const& element)
    {
        if (m_impl->shuttingDown) return;

        ThreadContext& context = m_impl->ContextForCurrentThread(element);
        Impl::Track(context, element);

        if (auto set = m_impl->Current()) Impl::ApplyRules(context, element, *set);
    }

    void Engine::Apply(SettingsDocument document)
    {
        if (m_impl->shuttingDown) return;

        auto set = std::make_shared<RuleSet>();
        set->appliedAt = Clock::now();
        set->problems = std::move(document.problems);
        set->clock = std::make_shared<const ClockConfiguration>(std::move(document.clock));
        set->startMenuHeight = document.startMenuHeight.enabled;
        set->startMenuWidth = document.startMenuWidth;

        for (RuleDefinition& definition : document.rules)
        {
            auto rule = std::make_shared<RuleRuntime>();
            rule->definition = std::move(definition);

            if (rule->definition.target.empty()) rule->problem = L"the rule has no target";
            else if (rule->definition.property.empty()) rule->problem = L"the rule has no property";
            else if (!ParseSelector(rule->definition.target, rule->selector, rule->problem)) rule->problem = L"target: " + rule->problem;
            else if (!rule->definition.visualState.empty() && rule->selector.path.back().visualStateGroup.empty())
                rule->problem = L"a rule with a visualState needs @Group on the last element of its target";

            if (!rule->problem.empty())
            {
                Log(LogLevel::Warning, L"rule_invalid", L"A rule cannot be applied as written",
                    { { L"rule", rule->definition.id }, { L"problem", rule->problem } });
            }
            set->rules.push_back(std::move(rule));
        }

        {
            std::lock_guard guard(m_impl->lock);
            set->generation = ++m_impl->generation;
            m_impl->current = set;
        }

        if (!m_impl->ticker.joinable())
        {
            m_impl->ticker = std::thread([impl = m_impl] { impl->TickerMain(); });
        }

        std::shared_ptr<const RuleSet> shared = set;
        size_t unreachable = 0;
        for (ThreadContext* context : m_impl->Contexts())
        {
            bool done = Impl::RunOn(*context, [context, shared] { Impl::Reapply(*context, shared); }, kWaitForUiThread);
            if (!done)
            {
                ++unreachable;
                Log(LogLevel::Warning, L"ui_thread_busy", L"A XAML thread did not take the new document in time",
                    { { L"tid", std::to_wstring(context->tid) } });
            }
        }

        Log(LogLevel::Info, L"document_applied", L"Applied a settings document",
            { { L"generation", std::to_wstring(set->generation) }, { L"rules", std::to_wstring(set->rules.size()) },
              { L"clock", set->clock->enabled ? L"on" : L"off" }, { L"start_menu_height", set->startMenuHeight ? L"on" : L"off" }, { L"start_menu_width", !set->startMenuWidth.enabled ? L"off" : set->startMenuWidth.fill ? L"fill" : L"value" }, { L"unreachable_threads", std::to_wstring(unreachable) } });
        m_impl->SendStatus(true);
    }

    size_t Engine::Park()
    {
        {
            std::lock_guard guard(m_impl->lock);
            m_impl->current.reset();
        }

        auto restored = std::make_shared<std::atomic<size_t>>(0);
        for (ThreadContext* context : m_impl->Contexts())
        {
            bool done = Impl::RunOn(*context, [context, restored]
            {
                *restored += Impl::ClearAll(*context);
            }, kWaitForUiThread);

            if (!done)
            {
                Log(LogLevel::Warning, L"ui_thread_busy", L"A XAML thread did not give its overrides back in time",
                    { { L"tid", std::to_wstring(context->tid) } });
            }
        }
        return restored->load();
    }

    size_t Engine::Shutdown()
    {
        m_impl->shuttingDown = true;
        {
            std::lock_guard guard(m_impl->lock);
            m_impl->current.reset();
        }

        SetEvent(m_impl->tickerStop);
        if (m_impl->ticker.joinable()) m_impl->ticker.join();

        auto restored = std::make_shared<std::atomic<size_t>>(0);
        for (ThreadContext* context : m_impl->Contexts())
        {
            bool done = Impl::RunOn(*context, [context, restored]
            {
                *restored += Impl::ClearAll(*context);
                context->tracked.clear();
            }, kWaitForUiThread);

            if (!done)
            {
                Log(LogLevel::Warning, L"ui_thread_busy", L"A XAML thread did not give its overrides back in time",
                    { { L"tid", std::to_wstring(context->tid) } });
            }
        }
        return restored->load();
    }

    void Engine::DumpTrees()
    {
        auto roots = std::make_shared<std::atomic<int>>(0);
        for (ThreadContext* context : m_impl->Contexts())
        {
            Impl::RunOn(*context, [context, roots]
            {
                std::vector<std::pair<void*, wux::DependencyObject>> found;
                for (Tracked const& entry : context->tracked)
                {
                    auto element = entry.element.get();
                    if (!element) continue;

                    wux::DependencyObject top = element;
                    for (auto parent = ParentOf(top); parent; parent = ParentOf(top)) top = parent;

                    void* key = Identity(top);
                    if (std::none_of(found.begin(), found.end(), [key](auto const& item) { return item.first == key; }))
                        found.emplace_back(key, top);
                }

                for (auto const& [key, root] : found)
                {
                    std::wstring text;
                    DumpElement(text, root, 0);

                    json::JsonObject record;
                    record.SetNamedValue(L"t", Text(L"tree"));
                    record.SetNamedValue(L"tid", Text(std::to_wstring(context->tid)));
                    record.SetNamedValue(L"text", Text(text));
                    Post(record);
                    ++*roots;
                }
            }, kWaitForUiThread);
        }

        json::JsonObject end;
        end.SetNamedValue(L"t", Text(L"tree_end"));
        end.SetNamedValue(L"roots", Text(std::to_wstring(roots->load())));
        Post(end);
    }
}
