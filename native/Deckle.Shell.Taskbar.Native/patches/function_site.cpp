#include "pch.h"
#include "function_site.h"

namespace
{
    namespace json = winrt::Windows::Data::Json;
    using deckle::patches::FunctionSite;

    bool ReadHex(std::wstring_view hex, std::array<uint8_t, FunctionSite::PrologueLength>& bytes)
    {
        if (hex.size() != bytes.size() * 2) return false;
        for (size_t i = 0; i < bytes.size(); ++i)
        {
            auto digit = [](wchar_t c) -> int
            {
                if (c >= L'0' && c <= L'9') return c - L'0';
                if (c >= L'A' && c <= L'F') return c - L'A' + 10;
                if (c >= L'a' && c <= L'f') return c - L'a' + 10;
                return -1;
            };
            int high = digit(hex[2 * i]), low = digit(hex[2 * i + 1]);
            if (high < 0 || low < 0) return false;
            bytes[i] = static_cast<uint8_t>(high * 16 + low);
        }
        return true;
    }

    std::wstring Text(json::JsonObject const& entry, std::wstring_view key)
    {
        return entry.HasKey(key) && entry.GetNamedValue(key).ValueType() == json::JsonValueType::String
            ? std::wstring(entry.GetNamedString(key)) : std::wstring();
    }
}

namespace deckle::patches
{
    bool ReadNumber(json::JsonObject const& entry, std::wstring_view key, double& value)
    {
        if (!entry.HasKey(key) || entry.GetNamedValue(key).ValueType() != json::JsonValueType::Number) return false;
        value = entry.GetNamedNumber(key);
        return true;
    }

    bool ReadSite(json::JsonObject const& entry, FunctionSite& site)
    {
        site.id = Text(entry, L"id");
        site.module = Text(entry, L"module");
        double timestamp = 0, imageSize = 0, rva = 0;
        bool complete = !site.id.empty() && !site.module.empty()
            && ReadNumber(entry, L"timestamp", timestamp) && ReadNumber(entry, L"imageSize", imageSize)
            && ReadNumber(entry, L"rva", rva) && ReadHex(Text(entry, L"original"), site.original);
        if (!complete) return false;

        site.timestamp = static_cast<uint32_t>(timestamp);
        site.imageSize = static_cast<uint32_t>(imageSize);
        site.rva = static_cast<uint32_t>(rva);
        return true;
    }

    uint8_t* Locate(FunctionSite const& site, std::wstring& reason)
    {
        HMODULE module = GetModuleHandleW(site.module.c_str());
        if (!module) { reason = L"module_not_loaded"; return nullptr; }

        auto* base = reinterpret_cast<uint8_t*>(module);
        auto* dos = reinterpret_cast<IMAGE_DOS_HEADER const*>(base);
        auto* nt = reinterpret_cast<IMAGE_NT_HEADERS const*>(base + dos->e_lfanew);
        if (nt->FileHeader.TimeDateStamp != site.timestamp || nt->OptionalHeader.SizeOfImage != site.imageSize)
        {
            reason = L"other_build";
            return nullptr;
        }

        uint8_t* address = base + site.rva;
        DWORD64 imageBase = 0;
        PRUNTIME_FUNCTION function = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(address), &imageBase, nullptr);
        if (!function || function->BeginAddress != site.rva || function->EndAddress - function->BeginAddress < FunctionSite::PrologueLength)
        {
            reason = L"not_a_function_start";
            return nullptr;
        }
        return address;
    }
}
