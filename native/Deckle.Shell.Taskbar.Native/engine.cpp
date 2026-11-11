#include "pch.h"
#include "engine.h"
#include "channel.h"
#include "log.h"
#include "util.h"
#include "residency.h"
#include "xamldiag/tap_connection.h"
#include "xamldiag/visual_tree_watcher.h"
#include "xamldiag/rules/rule_document.h"
#include "xamldiag/rules/rule_engine.h"
#include "patches/constant_return.h"

namespace
{
    namespace json = winrt::Windows::Data::Json;

    constexpr int ProtocolVersion = 1;

    HMODULE g_module = nullptr;
    HMODULE g_pin = nullptr;                 // the extra reference that keeps the DLL mapped
    std::atomic<bool> g_started{false};
    std::atomic<bool> g_stopping{false};
    HANDLE g_stopEvent = nullptr;            // manual-reset, signalled by RequestStop, reset at each session
    std::wstring g_stopReason;
    std::mutex g_stopReasonLock;

    // The functions the patches change are ones the taskbar reads when it lays
    // itself out (its thickness). The Settings app sends this same message when
    // a taskbar setting changes: the taskbar lays itself out again, and the work
    // area follows. Sent, not posted: the text travels by pointer.
    void RelayOutTaskbar()
    {
        HWND taskbar = FindWindowW(L"Shell_TrayWnd", nullptr);
        if (!taskbar) return;
        DWORD_PTR result = 0;
        SendMessageTimeoutW(taskbar, WM_SETTINGCHANGE, 0, reinterpret_cast<LPARAM>(L"TraySettings"),
                            SMTO_ABORTIFHUNG, 2000, &result);
    }

    // Restores everything the engine changed in explorer: the rule engine gives
    // back every property it holds, the patches every function's own code.
    size_t RestoreAll()
    {
        size_t functions = deckle::patches::RestoreAll();
        if (functions > 0) RelayOutTaskbar();
        return functions + deckle::rules::Engine::Instance().Shutdown();
    }

    std::wstring ReadString(json::JsonObject const& object, std::wstring_view key)
    {
        if (!object.HasKey(key)) return {};
        auto value = object.GetNamedValue(key);
        return value.ValueType() == json::JsonValueType::String ? std::wstring(value.GetString()) : std::wstring();
    }

    // One line from Deckle. Returns false to end the pump.
    bool HandleCommand(std::string_view line)
    {
        json::JsonObject command{ nullptr };
        if (!json::JsonObject::TryParse(winrt::hstring(deckle::util::FromUtf8(line)), command))
        {
            deckle::Log(deckle::LogLevel::Warning, L"command_unreadable", L"Deckle sent a line that is not a JSON object",
                        { { L"bytes", std::to_wstring(line.size()) } });
            return true;
        }

        const std::wstring kind = ReadString(command, L"t");
        if (kind == L"stop")
        {
            deckle::engine::RequestStop(L"stop_command");
            return false;
        }
        if (kind == L"apply")
        {
            // The settings document: rules and behaviors. A new one replaces the
            // last, which is restored first.
            deckle::Log(deckle::LogLevel::Info, L"document_received", L"Received a settings document",
                        { { L"bytes", std::to_wstring(line.size()) } });
            if (!command.HasKey(L"doc") || command.GetNamedValue(L"doc").ValueType() != json::JsonValueType::Object)
            {
                deckle::Log(deckle::LogLevel::Warning, L"document_unreadable", L"The apply command carries no document");
                return true;
            }
            auto document = command.GetNamedObject(L"doc");
            std::vector<std::wstring> problems;
            if (deckle::patches::Apply(deckle::patches::Parse(document, problems))) RelayOutTaskbar();
            for (auto const& problem : problems)
                deckle::Log(deckle::LogLevel::Warning, L"document_problem", L"Part of the settings document could not be read", { { L"problem", problem } });
            deckle::rules::Engine::Instance().Apply(deckle::rules::ParseSettingsDocument(document));
            return true;
        }
        if (kind == L"dump")
        {
            deckle::rules::Engine::Instance().DumpTrees();
            return true;
        }
        if (kind == L"ping")
        {
            deckle::Log(deckle::LogLevel::Info, L"pong", L"Answering a ping");
            return true;
        }

        deckle::Log(deckle::LogLevel::Warning, L"command_unknown", L"Deckle sent a command this DLL does not know",
                    { { L"t", kind } });
        return true;
    }

    void SendHello()
    {
        json::JsonObject hello;
        hello.SetNamedValue(L"t", json::JsonValue::CreateStringValue(L"hello"));
        hello.SetNamedValue(L"protocol", json::JsonValue::CreateStringValue(std::to_wstring(ProtocolVersion)));
        hello.SetNamedValue(L"pid", json::JsonValue::CreateStringValue(std::to_wstring(GetCurrentProcessId())));
        hello.SetNamedValue(L"module", json::JsonValue::CreateStringValue(deckle::util::ModulePath(g_module)));
        deckle::Channel::Instance().Post(deckle::util::ToUtf8(hello.Stringify()));
    }

    // Undoes what the worker did and gives the DLL back to Windows. Only while
    // XAML Diagnostics never answered in this process: past that point the DLL
    // parks instead (residency.h). Runs on the worker thread and does not
    // return when the unload succeeds.
    [[noreturn]] void Shutdown(std::wstring_view reason)
    {
        using deckle::Log;
        using deckle::LogLevel;
        auto& channel = deckle::Channel::Instance();

        Log(LogLevel::Info, L"stopping", L"Stopping the taskbar engine", { { L"reason", std::wstring(reason) } });
        channel.Flush(std::chrono::seconds(1));

        size_t restored = RestoreAll();
        Log(LogLevel::Info, L"restored", L"Gave back everything the engine changed",
            { { L"parts", std::to_wstring(restored) } });

        deckle::xamldiag::DetachWatcher();
        channel.Flush(std::chrono::seconds(1));

        // Nothing of XAML's holds this copy: the next one may claim the process.
        deckle::residency::Release();

        // Objects this DLL created for explorer (the TAP site, the watcher)
        // carry its code. The DLL may only go once none is alive.
        bool clear = false;
        for (int waited = 0; waited < 100; ++waited)   // 5 s
        {
            if (winrt::get_module_lock() == 0) { clear = true; break; }
            Sleep(50);
        }

        if (!clear)
        {
            Log(LogLevel::Warning, L"unload_deferred", L"The DLL stays loaded: objects it created are still alive",
                { { L"live_objects", std::to_wstring(winrt::get_module_lock()) } });
            channel.Flush(std::chrono::seconds(2));
            channel.Close();
            ExitThread(0);   // the pin stays; the file stays locked until explorer restarts
        }

        Log(LogLevel::Info, L"unloading", L"Unloading the DLL from explorer");
        channel.Flush(std::chrono::seconds(2));
        channel.Close();

        // Drops the pin and ends this thread in one step: the thread's code
        // lives in the DLL, so it cannot return into it after the unmap.
        FreeLibraryAndExitThread(g_pin, 0);
    }

    // Gives back what the engine changed and waits in the process for the next
    // Deckle, with XAML Diagnostics and the visual tree watcher left as they
    // are (residency.h says why). Runs on the worker thread at the end of a
    // session; returns once a later copy handed over a pipe.
    std::wstring Park(std::wstring_view reason)
    {
        using deckle::Log;
        using deckle::LogLevel;
        auto& channel = deckle::Channel::Instance();

        Log(LogLevel::Info, L"stopping", L"Stopping the taskbar engine", { { L"reason", std::wstring(reason) } });

        size_t functions = deckle::patches::RestoreAll();
        if (functions > 0) RelayOutTaskbar();
        size_t restored = functions + deckle::rules::Engine::Instance().Park();
        Log(LogLevel::Info, L"restored", L"Gave back everything the engine changed",
            { { L"parts", std::to_wstring(restored) } });

        if (deckle::residency::CanBeWoken())
        {
            Log(LogLevel::Info, L"parked", L"The engine stays in this process, idle, until Deckle connects again");
        }
        else
        {
            Log(LogLevel::Warning, L"parked_unreachable",
                L"The engine stays in this process, idle, and no later Deckle can reach it before the process restarts",
                { { L"pid", std::to_wstring(GetCurrentProcessId()) } });
        }
        channel.Flush(std::chrono::seconds(2));
        channel.Close();
        channel.SetAccepting(false);

        std::wstring pipe = deckle::residency::ParkUntilHandedOver();

        channel.SetAccepting(true);
        return pipe;
    }

    std::wstring StopReason(deckle::Channel::EndReason reason)
    {
        std::lock_guard guard(g_stopReasonLock);
        if (!g_stopReason.empty()) return g_stopReason;
        return reason == deckle::Channel::EndReason::Disconnected ? L"pipe_disconnected" : L"cancelled";
    }

    // One conversation with one Deckle, on a connected channel. Returns why it
    // ended.
    std::wstring RunSession(int session)
    {
        using deckle::Log;
        using deckle::LogLevel;

        SendHello();
        Log(LogLevel::Info, L"connected", L"Connected to Deckle", { { L"session", std::to_wstring(session) } });

        // The XAML Diagnostics connection, on a thread of its own: it can take
        // up to the whole retry budget when the XAML runtime is not up yet.
        // Once per process: a later session finds the watcher still advised.
        std::thread connect;
        if (!deckle::xamldiag::DiagnosticsAnswered())
        {
            connect = std::thread([]
            {
                HRESULT hr = deckle::xamldiag::ConnectToXamlDiagnostics();
                if (FAILED(hr) && hr != E_ABORT)
                {
                    deckle::Log(deckle::LogLevel::Error, L"tap_failed", L"Could not connect to XAML Diagnostics",
                                { { L"hr", deckle::util::HandleText(static_cast<uint32_t>(hr)) } });
                }
            });
        }
        else
        {
            Log(LogLevel::Info, L"tap_kept", L"Kept the XAML Diagnostics connection of an earlier session");
        }

        auto reason = deckle::Channel::Instance().Pump(g_stopEvent, HandleCommand);

        g_stopping = true;
        SetEvent(g_stopEvent);
        if (connect.joinable()) connect.join();
        return StopReason(reason);
    }

    DWORD WINAPI WorkerMain(LPVOID)
    {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        using deckle::Log;
        using deckle::LogLevel;
        auto& channel = deckle::Channel::Instance();

        Log(LogLevel::Info, L"loaded", L"Taskbar engine loaded in explorer",
            { { L"pid", std::to_wstring(GetCurrentProcessId()) },
              { L"process", deckle::util::ProcessImagePath() },
              { L"xaml_modules", deckle::util::XamlModules() },
              { L"module", deckle::util::ModulePath(g_module) } });

        std::wstring pipe = deckle::util::PipeName(g_module);
        for (int session = 1;; ++session)
        {
            // No pipe: the first Deckle never served one, or the Deckle that
            // woke this engine left before it connected.
            std::wstring why = channel.Connect(pipe, std::chrono::seconds(10), g_stopEvent)
                ? RunSession(session)
                : L"no_pipe";

            // XAML Diagnostics never answered (the session ended before the
            // runtime was up, or there was none): nothing of XAML's holds the
            // DLL, so it leaves rather than sit in explorer unobserved.
            if (!deckle::xamldiag::DiagnosticsAnswered())
            {
                g_stopping = true;
                Shutdown(why);
            }

            pipe = Park(why);

            {
                std::lock_guard guard(g_stopReasonLock);
                g_stopReason.clear();
            }
            ResetEvent(g_stopEvent);
            g_stopping = false;
        }
    }

    // The worker of a copy that found another engine serving this process:
    // tells its own Deckle why it leaves, then leaves without touching XAML.
    DWORD WINAPI RefusedMain(LPVOID)
    {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        auto& channel = deckle::Channel::Instance();

        deckle::Log(deckle::LogLevel::Warning, L"engine_busy",
                    L"Another Deckle engine is serving this process, so this copy leaves without touching XAML",
                    { { L"pid", std::to_wstring(GetCurrentProcessId()) },
                      { L"module", deckle::util::ModulePath(g_module) } });

        if (channel.Connect(deckle::util::PipeName(g_module), std::chrono::seconds(10), g_stopEvent))
        {
            channel.Flush(std::chrono::seconds(2));
            channel.Close();
        }
        FreeLibraryAndExitThread(g_pin, 0);
    }
}

namespace deckle::engine
{
    void SetModule(HMODULE module) { g_module = module; }
    HMODULE Module() { return g_module; }

    UINT InjectMessage()
    {
        static const UINT message = RegisterWindowMessageW(L"Deckle.Taskbar.Inject.v1");
        return message;
    }

    bool IsStopping() { return g_stopping.load(); }

    void RequestStop(std::wstring_view reason)
    {
        {
            std::lock_guard guard(g_stopReasonLock);
            if (g_stopReason.empty()) g_stopReason = reason;
        }
        g_stopping = true;
        if (g_stopEvent) SetEvent(g_stopEvent);
    }

    void Start()
    {
        if (g_started.exchange(true))
        {
            // Deckle injected this same file again. When the engine is parked
            // here, a Deckle is waiting on its pipe: the engine goes to it.
            residency::HandOverToSelf(util::PipeName(g_module));
            return;
        }

        // Pin: the hook's own reference ends when the hook is removed.
        const std::wstring path = util::ModulePath(g_module);
        g_pin = LoadLibraryW(path.c_str());
        if (!g_pin)
        {
            g_started = false;
            return;
        }

        const residency::Claim claim = residency::ClaimOrHandOver(util::PipeName(g_module));
        if (claim == residency::Claim::HandedOver)
        {
            // Another copy is this process's engine and now serves this copy's
            // Deckle. This one never runs: it leaves with the hook.
            FreeLibrary(g_pin);
            g_pin = nullptr;
            return;
        }

        g_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        LPTHREAD_START_ROUTINE main = claim == residency::Claim::Owner ? WorkerMain : RefusedMain;
        HANDLE thread = g_stopEvent ? CreateThread(nullptr, 0, main, nullptr, 0, nullptr) : nullptr;
        if (!thread)
        {
            if (claim == residency::Claim::Owner) residency::Release();
            FreeLibrary(g_pin);
            g_pin = nullptr;
            g_started = false;
            return;
        }
        CloseHandle(thread);
    }
}


