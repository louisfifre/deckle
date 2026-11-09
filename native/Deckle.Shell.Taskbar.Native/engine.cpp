#include "pch.h"
#include "engine.h"
#include "channel.h"
#include "log.h"
#include "util.h"
#include "xamldiag/tap_connection.h"
#include "xamldiag/visual_tree_watcher.h"
#include "xamldiag/rules/rule_document.h"
#include "xamldiag/rules/rule_engine.h"

namespace
{
    namespace json = winrt::Windows::Data::Json;

    constexpr int ProtocolVersion = 1;

    HMODULE g_module = nullptr;
    HMODULE g_pin = nullptr;                 // the extra reference that keeps the DLL mapped
    std::atomic<bool> g_started{false};
    std::atomic<bool> g_stopping{false};
    HANDLE g_stopEvent = nullptr;            // manual-reset, signalled by RequestStop
    std::wstring g_stopReason;
    std::mutex g_stopReasonLock;

    // Restores everything the engine changed in explorer: the rule engine gives
    // back every property it holds. The parts that will change something else
    // (tier 3 hooks) register here, each with its own restore.
    size_t RestoreAll()
    {
        return deckle::rules::Engine::Instance().Shutdown();
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
            deckle::rules::Engine::Instance().Apply(deckle::rules::ParseSettingsDocument(command.GetNamedObject(L"doc")));
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

    // Undoes what the worker did and gives the DLL back to Windows. Runs on the
    // worker thread and does not return when the unload succeeds.
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

        if (!channel.Connect(std::chrono::seconds(10), g_stopEvent))
        {
            // Nobody to report to. There is no log to write to either, so the
            // DLL leaves explorer at once rather than sit there unobserved.
            OutputDebugStringW(L"Deckle.Shell.Taskbar.Native: no Deckle pipe, unloading\n");
            g_stopping = true;
            Shutdown(L"no_pipe");
        }

        SendHello();
        Log(LogLevel::Info, L"connected", L"Connected to Deckle");

        // The XAML Diagnostics connection, on a thread of its own: it can take
        // up to the whole retry budget when the XAML runtime is not up yet.
        std::thread connect([]
        {
            HRESULT hr = deckle::xamldiag::ConnectToXamlDiagnostics();
            if (FAILED(hr) && hr != E_ABORT)
            {
                deckle::Log(deckle::LogLevel::Error, L"tap_failed", L"Could not connect to XAML Diagnostics",
                            { { L"hr", deckle::util::HandleText(static_cast<uint32_t>(hr)) } });
            }
        });

        auto reason = channel.Pump(g_stopEvent, HandleCommand);

        g_stopping = true;
        SetEvent(g_stopEvent);
        if (connect.joinable()) connect.join();

        std::wstring why;
        {
            std::lock_guard guard(g_stopReasonLock);
            why = g_stopReason;
        }
        if (why.empty())
        {
            why = reason == deckle::Channel::EndReason::Disconnected ? L"pipe_disconnected" : L"cancelled";
        }

        Shutdown(why);
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
        if (g_started.exchange(true)) return;

        // Pin: the hook's own reference ends when the hook is removed.
        const std::wstring path = util::ModulePath(g_module);
        g_pin = LoadLibraryW(path.c_str());
        g_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

        HANDLE thread = g_pin && g_stopEvent ? CreateThread(nullptr, 0, WorkerMain, nullptr, 0, nullptr) : nullptr;
        if (!thread)
        {
            if (g_pin) FreeLibrary(g_pin);
            g_pin = nullptr;
            g_started = false;
            return;
        }
        CloseHandle(thread);
    }
}


