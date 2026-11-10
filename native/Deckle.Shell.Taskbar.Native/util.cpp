#include "pch.h"
#include "util.h"
#include <tlhelp32.h>

namespace deckle::util
{
    std::string ToUtf8(std::wstring_view text)
    {
        if (text.empty()) return {};
        int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
        std::string result(static_cast<size_t>(size), '\0');
        WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), size, nullptr, nullptr);
        return result;
    }

    std::wstring FromUtf8(std::string_view text)
    {
        if (text.empty()) return {};
        int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring result(static_cast<size_t>(size), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), size);
        return result;
    }

    namespace
    {
        // The instance named by the file beside the DLL, or empty. Only letters,
        // digits and dashes, at most 32: anything else is ignored, so a stray file
        // can never turn the pipe's name into a path.
        std::wstring InstanceName(HMODULE module)
        {
            std::wstring path = ModulePath(module);
            size_t slash = path.find_last_of(L"\\/");
            if (slash == std::wstring::npos) return {};
            path.resize(slash + 1);
            path += InstanceFileName;

            HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
            if (file == INVALID_HANDLE_VALUE) return {};
            char buffer[33]{};
            DWORD read = 0;
            BOOL ok = ReadFile(file, buffer, 32, &read, nullptr);
            CloseHandle(file);
            if (!ok) return {};

            std::wstring name;
            for (DWORD i = 0; i < read; ++i)
            {
                char c = buffer[i];
                if (c == '\r' || c == '\n' || c == ' ') break;
                bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
                if (!allowed) return {};
                name += static_cast<wchar_t>(c);
            }
            return name;
        }
    }

    std::wstring PipeName(HMODULE module)
    {
        DWORD session = 0;
        ProcessIdToSessionId(GetCurrentProcessId(), &session);
        std::wstring name = L"\\\\.\\pipe\\Deckle.Taskbar." + std::to_wstring(session);

        // One pipe per target process: explorer keeps the bare name, any other
        // host (the Start menu's StartMenuExperienceHost) gets its executable's
        // name appended, so two engines in two processes never share a pipe.
        // Deckle's side spells the same (TaskbarTarget.PipeName).
        std::wstring image = ProcessImagePath();
        size_t slash = image.find_last_of(L"\\/");
        std::wstring file = slash == std::wstring::npos ? image : image.substr(slash + 1);
        size_t dot = file.find_last_of(L'.');
        if (dot != std::wstring::npos) file.resize(dot);
        if (!file.empty() && _wcsicmp(file.c_str(), L"explorer") != 0) name += L"." + file;

        // A side instance (the development console) last, so it never meets the app's pipe.
        std::wstring instance = InstanceName(module);
        if (!instance.empty()) name += L"." + instance;
        return name;
    }

    std::wstring ModulePath(HMODULE module)
    {
        std::wstring path(MAX_PATH, L'\0');
        for (;;)
        {
            DWORD length = GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
            if (length == 0) return {};
            if (length < path.size())
            {
                path.resize(length);
                return path;
            }
            path.resize(path.size() * 2);
        }
    }

    std::wstring XamlModules()
    {
        std::wstring names;
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, GetCurrentProcessId());
        if (snapshot == INVALID_HANDLE_VALUE) return names;

        MODULEENTRY32W entry{ sizeof(entry) };
        for (BOOL more = Module32FirstW(snapshot, &entry); more; more = Module32NextW(snapshot, &entry))
        {
            std::wstring name = entry.szModule;
            std::wstring lower = name;
            for (wchar_t& c : lower) c = static_cast<wchar_t>(towlower(c));
            if (lower.find(L"xaml") != std::wstring::npos)
            {
                if (!names.empty()) names += L",";
                names += name;
            }
        }
        CloseHandle(snapshot);
        return names;
    }

    std::wstring ProcessImagePath()
    {
        return ModulePath(nullptr);
    }

    std::wstring HandleText(uint64_t value)
    {
        wchar_t buffer[24];
        swprintf_s(buffer, L"0x%016llX", static_cast<unsigned long long>(value));
        return buffer;
    }
}

