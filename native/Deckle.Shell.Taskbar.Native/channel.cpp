#include "pch.h"
#include "channel.h"
#include "util.h"
#include "engine.h"

namespace deckle
{
    Channel& Channel::Instance()
    {
        // Leaked on purpose: a function-local static would be destroyed during
        // the CRT's exit of explorer.exe, possibly after this DLL is gone.
        static Channel* instance = new Channel();
        return *instance;
    }

    Channel::Channel()
    {
        m_queueEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        m_readEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        m_writeEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    }

    Channel::~Channel() = default;

    void Channel::Post(std::string line)
    {
        {
            std::lock_guard guard(m_lock);
            if (m_queue.size() >= MaxQueued)
            {
                m_queue.pop_front();
                ++m_dropped;
            }
            m_queue.push_back(std::move(line));
        }
        SetEvent(m_queueEvent);
    }

    bool Channel::Connect(std::chrono::milliseconds timeout, HANDLE cancelEvent)
    {
        const std::wstring name = util::PipeName(engine::Module());
        const auto deadline = std::chrono::steady_clock::now() + timeout;

        for (;;)
        {
            HANDLE pipe = CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                                      OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
            if (pipe != INVALID_HANDLE_VALUE)
            {
                m_pipe = pipe;
                return true;
            }

            if (std::chrono::steady_clock::now() >= deadline) return false;

            if (GetLastError() == ERROR_PIPE_BUSY)
            {
                // The server exists and is serving another client: wait for a free instance.
                WaitNamedPipeW(name.c_str(), 200);
            }
            else if (WaitForSingleObject(cancelEvent, 200) == WAIT_OBJECT_0)
            {
                return false;
            }
        }
    }

    bool Channel::WriteAll(const std::string& line)
    {
        std::string payload = line;
        payload.push_back('\n');

        OVERLAPPED overlapped{};
        overlapped.hEvent = m_writeEvent;
        ResetEvent(m_writeEvent);

        DWORD written = 0;
        if (!WriteFile(m_pipe, payload.data(), static_cast<DWORD>(payload.size()), nullptr, &overlapped))
        {
            if (GetLastError() != ERROR_IO_PENDING) return false;
        }

        // A server that stopped reading must not hold this thread forever.
        if (WaitForSingleObject(m_writeEvent, 5000) != WAIT_OBJECT_0)
        {
            CancelIoEx(m_pipe, &overlapped);
            GetOverlappedResult(m_pipe, &overlapped, &written, TRUE);
            return false;
        }
        return GetOverlappedResult(m_pipe, &overlapped, &written, FALSE) && written == payload.size();
    }

    bool Channel::DrainQueue()
    {
        std::deque<std::string> batch;
        size_t dropped = 0;
        {
            std::lock_guard guard(m_lock);
            batch.swap(m_queue);
            dropped = std::exchange(m_dropped, 0);
        }

        if (dropped > 0)
        {
            batch.push_front("{\"t\":\"log\",\"level\":\"warning\",\"event\":\"log_dropped\","
                             "\"msg\":\"Log records were dropped before they reached Deckle\","
                             "\"fields\":{\"count\":\"" + std::to_string(dropped) + "\"}}");
        }

        for (const std::string& line : batch)
        {
            if (!WriteAll(line)) return false;
        }
        return true;
    }

    Channel::EndReason Channel::Pump(HANDLE cancelEvent, const std::function<bool(std::string_view)>& onLine)
    {
        char buffer[4096];
        std::string pending;
        OVERLAPPED read{};
        read.hEvent = m_readEvent;
        bool readInFlight = false;

        auto issueRead = [&]() -> bool
        {
            ResetEvent(m_readEvent);
            read = OVERLAPPED{};
            read.hEvent = m_readEvent;
            if (!ReadFile(m_pipe, buffer, sizeof(buffer), nullptr, &read))
            {
                if (GetLastError() != ERROR_IO_PENDING) return false;
            }
            readInFlight = true;
            return true;
        };

        // The read must be finished or cancelled before `read` and `buffer` leave scope.
        auto finish = [&](EndReason reason) -> EndReason
        {
            if (readInFlight)
            {
                CancelIoEx(m_pipe, &read);
                DWORD ignored = 0;
                GetOverlappedResult(m_pipe, &read, &ignored, TRUE);
                readInFlight = false;
            }
            return reason;
        };

        if (!issueRead()) return finish(EndReason::Disconnected);

        // A record posted before the loop started is already waiting.
        SetEvent(m_queueEvent);

        for (;;)
        {
            HANDLE waits[] = { cancelEvent, m_queueEvent, m_readEvent };
            DWORD which = WaitForMultipleObjects(3, waits, FALSE, INFINITE);

            if (which == WAIT_OBJECT_0) return finish(EndReason::Cancelled);

            if (which == WAIT_OBJECT_0 + 1)
            {
                if (!DrainQueue()) return finish(EndReason::Disconnected);
                continue;
            }

            if (which != WAIT_OBJECT_0 + 2) return finish(EndReason::Disconnected);

            DWORD count = 0;
            readInFlight = false;
            if (!GetOverlappedResult(m_pipe, &read, &count, FALSE) || count == 0)
            {
                return finish(EndReason::Disconnected);
            }

            pending.append(buffer, count);
            size_t newline;
            while ((newline = pending.find('\n')) != std::string::npos)
            {
                std::string_view line(pending.data(), newline);
                if (!line.empty() && line.back() == '\r') line.remove_suffix(1);
                bool keepGoing = line.empty() || onLine(line);
                pending.erase(0, newline + 1);
                if (!keepGoing) return finish(EndReason::Stop);
            }

            if (!issueRead()) return finish(EndReason::Disconnected);
        }
    }

    void Channel::Flush(std::chrono::milliseconds timeout)
    {
        if (m_pipe == INVALID_HANDLE_VALUE) return;
        // WriteAll bounds each write by its own timeout; the parameter caps the whole flush.
        const auto deadline = std::chrono::steady_clock::now() + timeout;
        while (std::chrono::steady_clock::now() < deadline)
        {
            {
                std::lock_guard guard(m_lock);
                if (m_queue.empty() && m_dropped == 0) return;
            }
            if (!DrainQueue()) return;
        }
    }

    void Channel::Close()
    {
        if (m_pipe != INVALID_HANDLE_VALUE)
        {
            CloseHandle(m_pipe);
            m_pipe = INVALID_HANDLE_VALUE;
        }
    }
}
