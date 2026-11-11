#include "pch.h"
#include "thumbnail_box.h"
#include "code_write.h"
#include "../log.h"

#include <cstring>

namespace
{
    namespace json = winrt::Windows::Data::Json;
    using deckle::patches::FunctionSite;
    using deckle::patches::ThumbnailBox;

    constexpr size_t PatchLength = FunctionSite::PrologueLength;
    using Bytes = std::array<uint8_t, PatchLength>;

    // winrt::Windows::Foundation::Size as Explorer lays it out: two floats.
    struct Size
    {
        float Width;
        float Height;
    };

    // The box in force, read by the function below on Explorer's UI thread.
    // Explorer's own values until a document says otherwise.
    std::atomic<float> g_maxWidth{ 200.0f };
    std::atomic<float> g_maxHeight{ 110.0f };

    // What Explorer's function does, with the box above in place of its two
    // constants. Read from its code on build 26100 (Taskbar.View.dll
    // 2608.26001.200.0), kept as it is down to the first branch: a window
    // already inside the box comes back untouched, not multiplied by the scale.
    //
    // The signature is the one Explorer calls: the result goes through a pointer
    // (rcx), the size arrives by value as its eight bytes (rdx), the scale is
    // the third argument (xmm2). It calls nothing outside this DLL, so a thread
    // is inside this DLL for as long as it is inside this function.
    Size* ScaledThumbnailSize(Size* result, uint64_t packedSize, float scale)
    {
        Size size;
        std::memcpy(&size, &packedSize, sizeof size);
        const float maxWidth = g_maxWidth.load(std::memory_order_relaxed);
        const float maxHeight = g_maxHeight.load(std::memory_order_relaxed);

        if (maxHeight > size.Height && maxWidth > size.Width)
        {
            *result = size;
            return result;
        }

        const float fit = (std::min)(1.0f, (std::min)(maxWidth / size.Width, maxHeight / size.Height));
        result->Width = size.Width * fit * scale;
        result->Height = size.Height * fit * scale;
        return result;
    }

    // mov rax, imm64 (48 B8 + 8 bytes) ; jmp rax (FF E0). The four bytes after
    // them are never run and stay as the function had them.
    Bytes PatchFor(Bytes const& original)
    {
        Bytes bytes = original;
        const auto target = reinterpret_cast<uint64_t>(&ScaledThumbnailSize);
        bytes[0] = 0x48;
        bytes[1] = 0xB8;
        std::memcpy(bytes.data() + 2, &target, sizeof target);
        bytes[10] = 0xFF;
        bytes[11] = 0xE0;
        return bytes;
    }

    std::mutex g_lock;
    std::wstring g_id;                // of the patch in place, under g_lock
    uint8_t* g_address = nullptr;     // null while the function has its own code
    Bytes g_original{};

    std::wstring Number(double value)
    {
        wchar_t text[32];
        swprintf_s(text, L"%g", value);
        return text;
    }

    void Report(deckle::LogLevel level, std::wstring_view event, std::wstring_view message, ThumbnailBox const& box,
                std::wstring_view reason = {})
    {
        deckle::Log(level, event, message,
                    { { L"id", box.site.id }, { L"module", box.site.module },
                      { L"max_width", Number(box.maxWidth) }, { L"max_height", Number(box.maxHeight) },
                      { L"reason", std::wstring(reason) } });
    }

    // Under g_lock. The write waits for a moment when no thread is in this DLL,
    // so none is left in the function the jump led to.
    bool RestoreLocked()
    {
        if (!g_address) return false;
        const bool restored = deckle::patches::WriteCode(g_address, g_original.data(), g_original.size(), { deckle::patches::OwnImage() });
        deckle::Log(restored ? deckle::LogLevel::Info : deckle::LogLevel::Error,
                    restored ? L"thumbnail_box_restored" : L"thumbnail_box_restore_failed",
                    restored ? L"Gave Explorer its own thumbnail size back" : L"Could not give Explorer its own thumbnail size back",
                    { { L"id", g_id } });
        if (restored) g_address = nullptr;
        return restored;
    }
}

namespace deckle::patches
{
    std::optional<ThumbnailBox> ParseThumbnailBox(json::JsonObject const& document, std::vector<std::wstring>& problems)
    {
        if (!document.HasKey(L"thumbnailBox")) return std::nullopt;
        if (document.GetNamedValue(L"thumbnailBox").ValueType() != json::JsonValueType::Object)
        {
            problems.push_back(L"thumbnailBox: not an object");
            return std::nullopt;
        }

        auto object = document.GetNamedObject(L"thumbnailBox");
        ThumbnailBox box;
        if (!ReadSite(object, box.site) || !ReadNumber(object, L"maxWidth", box.maxWidth) || !ReadNumber(object, L"maxHeight", box.maxHeight)
            || !(box.maxWidth > 0) || !(box.maxHeight > 0))
        {
            problems.push_back(L"thumbnailBox: a field is missing or not a size");
            return std::nullopt;
        }
        return box;
    }

    void ApplyThumbnailBox(std::optional<ThumbnailBox> const& box)
    {
        std::lock_guard guard(g_lock);
        if (!box)
        {
            RestoreLocked();
            return;
        }

        std::wstring reason;
        uint8_t* address = Locate(box->site, reason);
        if (!address)
        {
            Report(LogLevel::Warning, L"thumbnail_box_skipped", L"Left Explorer's thumbnail size alone: the function is not the one Deckle resolved", *box, reason);
            return;
        }

        Bytes current{};
        std::memcpy(current.data(), address, PatchLength);
        const Bytes patch = PatchFor(box->site.original);
        if (current != box->site.original && current != patch)
        {
            Report(LogLevel::Warning, L"thumbnail_box_skipped", L"Left Explorer's thumbnail size alone: its code is not what Deckle read", *box, L"unexpected_bytes");
            return;
        }

        // The box first: the function must never run with a box half written.
        g_maxWidth.store(static_cast<float>(box->maxWidth), std::memory_order_relaxed);
        g_maxHeight.store(static_cast<float>(box->maxHeight), std::memory_order_relaxed);

        if (current != patch && !WriteCode(address, patch.data(), patch.size()))
        {
            Report(LogLevel::Error, L"thumbnail_box_failed", L"Could not lead Explorer's thumbnail size to Deckle's", *box, L"write_failed");
            return;
        }

        g_id = box->site.id;
        g_address = address;
        g_original = box->site.original;
        Report(LogLevel::Info, L"thumbnail_box_applied", L"Explorer now sizes its thumbnails to Deckle's box", *box);
    }

    bool RestoreThumbnailBox()
    {
        std::lock_guard guard(g_lock);
        return RestoreLocked();
    }

    bool ThumbnailBoxInPlace()
    {
        std::lock_guard guard(g_lock);
        return g_address != nullptr;
    }
}
