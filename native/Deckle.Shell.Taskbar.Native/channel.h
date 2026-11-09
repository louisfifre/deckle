#pragma once

#include <functional>

namespace deckle
{
    // The DLL's one conversation with Deckle: a duplex named pipe that Deckle
    // serves and this DLL connects to, carrying one JSON document per line, in
    // UTF-8, both ways.
    //
    // Why a queue between Log() and the pipe: log records are produced on
    // explorer's UI threads, which must never wait for Deckle to read. Producers
    // only append to a bounded in-memory queue; the thread that owns the channel
    // writes the queue out. Until the pipe is connected the queue is the buffer,
    // so the records of the first moments are not lost. When the bound is hit
    // the oldest record goes and a `log_dropped` record reports the count.
    class Channel
    {
    public:
        enum class EndReason
        {
            Stop,          // a command asked to stop
            Disconnected,  // Deckle closed the pipe or stopped reading
            Cancelled,     // the cancel event was signalled
        };

        static Channel& Instance();

        // Any thread. Takes one JSON document without its line break.
        void Post(std::string line);

        // Connects to Deckle's pipe, retrying until the timeout while the
        // server does not exist yet. False on timeout or when cancelled.
        bool Connect(std::chrono::milliseconds timeout, HANDLE cancelEvent);

        // The owning thread's loop: writes what the queue holds, reads
        // commands and hands each line to `onLine`, which returns false to end
        // the loop. Returns why it ended.
        EndReason Pump(HANDLE cancelEvent, const std::function<bool(std::string_view)>& onLine);

        // Writes what is still queued, within the timeout.
        void Flush(std::chrono::milliseconds timeout);

        void Close();

    private:
        Channel();
        ~Channel();

        bool DrainQueue();
        bool WriteAll(const std::string& line);

        static constexpr size_t MaxQueued = 20000;

        std::mutex m_lock;
        std::deque<std::string> m_queue;
        size_t m_dropped = 0;

        HANDLE m_pipe = INVALID_HANDLE_VALUE;
        HANDLE m_queueEvent = nullptr;  // auto-reset, set by Post
        HANDLE m_readEvent = nullptr;   // manual-reset, for the overlapped read
        HANDLE m_writeEvent = nullptr;  // manual-reset, for the overlapped write
    };
}
