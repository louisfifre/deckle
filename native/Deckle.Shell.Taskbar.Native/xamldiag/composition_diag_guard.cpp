#include "pch.h"
#include "composition_diag_guard.h"

#include <cstring>

namespace
{
    constexpr wchar_t kXamlModule[] = L"Windows.UI.Xaml.dll";
    constexpr wchar_t kDebugKey[]   = L"Software\\Microsoft\\XAML\\Debug";
    constexpr wchar_t kValueName[]  = L"DisableCompositionDiag";

    thread_local bool t_reportDisabled = false;
    std::atomic<bool> g_answered{ false };

    decltype(&RegOpenKeyExW)    g_openOriginal     = nullptr;
    decltype(&RegQueryValueExW) g_queryOriginal    = nullptr;
    decltype(&RegGetValueW)     g_getValueOriginal = nullptr;

    bool IsValueName(LPCWSTR name)
    {
        return name && _wcsicmp(name, kValueName) == 0;
    }

    // Writes REG_DWORD 1 the way the registry would, ERROR_MORE_DATA included.
    LSTATUS AnswerOne(LPDWORD type, LPBYTE data, LPDWORD size)
    {
        g_answered = true;
        if (type) *type = REG_DWORD;
        if (data && (!size || *size < sizeof(DWORD)))
        {
            if (size) *size = sizeof(DWORD);
            return ERROR_MORE_DATA;
        }
        if (data) *reinterpret_cast<DWORD*>(data) = 1;
        if (size) *size = sizeof(DWORD);
        return ERROR_SUCCESS;
    }

    LSTATUS WINAPI OpenHook(HKEY root, LPCWSTR subKey, DWORD options, REGSAM access, PHKEY result)
    {
        LSTATUS status = g_openOriginal(root, subKey, options, access, result);
        if (status == ERROR_SUCCESS || !t_reportDisabled || root != HKEY_LOCAL_MACHINE ||
            !subKey || _wcsicmp(subKey, kDebugKey) != 0)
        {
            return status;
        }

        // The debug key usually does not exist, and the value is only queried
        // once the key opened, so a key that does exist stands in for it.
        return g_openOriginal(HKEY_LOCAL_MACHINE, L"Software\\Microsoft", options, access, result);
    }

    LSTATUS WINAPI QueryHook(HKEY key, LPCWSTR name, LPDWORD reserved, LPDWORD type, LPBYTE data, LPDWORD size)
    {
        if (!t_reportDisabled || !IsValueName(name))
        {
            return g_queryOriginal(key, name, reserved, type, data, size);
        }
        return AnswerOne(type, data, size);
    }

    LSTATUS WINAPI GetValueHook(HKEY key, LPCWSTR subKey, LPCWSTR name, DWORD flags, LPDWORD type, PVOID data, LPDWORD size)
    {
        if (!t_reportDisabled || !IsValueName(name))
        {
            return g_getValueOriginal(key, subKey, name, flags, type, data, size);
        }
        return AnswerOne(type, static_cast<LPBYTE>(data), size);
    }

    // The import address table slot through which `module` calls `function`,
    // whichever DLL it imports it from (an API set here).
    void** FindImportSlot(HMODULE module, char const* function)
    {
        auto* base = reinterpret_cast<uint8_t*>(module);
        auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
        auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
        auto const& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (!directory.VirtualAddress) return nullptr;

        for (auto* import = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + directory.VirtualAddress); import->Name; ++import)
        {
            if (!import->OriginalFirstThunk) continue;
            auto* names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + import->OriginalFirstThunk);
            auto* slots = reinterpret_cast<IMAGE_THUNK_DATA*>(base + import->FirstThunk);
            for (; names->u1.AddressOfData; ++names, ++slots)
            {
                if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
                auto* byName = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
                if (std::strcmp(reinterpret_cast<char const*>(byName->Name), function) == 0)
                {
                    return reinterpret_cast<void**>(&slots->u1.Function);
                }
            }
        }
        return nullptr;
    }

    // Points one slot at `hook`. The original is stored before the slot
    // changes, so a thread that calls through the new pointer already finds
    // it. One aligned pointer store: a concurrent caller reads either value.
    template <typename Fn>
    bool Redirect(HMODULE module, char const* function, Fn hook, Fn& original)
    {
        void** slot = FindImportSlot(module, function);
        if (!slot) return false;

        DWORD previous = 0;
        if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &previous)) return false;
        original = reinterpret_cast<Fn>(*slot);
        InterlockedExchangePointer(slot, reinterpret_cast<void*>(hook));
        VirtualProtect(slot, sizeof(void*), previous, &previous);
        return true;
    }

    std::mutex g_installLock;
    bool g_installed = false;
}

namespace deckle::xamldiag
{
    bool CompositionDiagGuard::Install()
    {
        std::lock_guard guard(g_installLock);
        if (g_installed) return true;

        HMODULE xaml = GetModuleHandleW(kXamlModule);
        if (!xaml) return false;

        // Either route is enough on its own: the open and query pair, or
        // RegGetValueW. Each slot stays redirected once set, even when another
        // is missing, so a second Install does not redirect it twice.
        // A redirected slot points into this DLL for the rest of the process,
        // so the DLL is pinned before the first one changes: an unload would
        // leave Windows.UI.Xaml.dll calling unmapped code, and a reload would
        // find its own hook in the slot and take it for the original.
        HMODULE self = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                                reinterpret_cast<LPCWSTR>(&OpenHook), &self))
        {
            return false;
        }

        static bool openDone = false, queryDone = false, getDone = false;
        if (!openDone)  openDone  = Redirect(xaml, "RegOpenKeyExW", &OpenHook, g_openOriginal);
        if (!queryDone) queryDone = Redirect(xaml, "RegQueryValueExW", &QueryHook, g_queryOriginal);
        if (!getDone)   getDone   = Redirect(xaml, "RegGetValueW", &GetValueHook, g_getValueOriginal);

        g_installed = (openDone && queryDone) || getDone;
        return g_installed;
    }

    bool CompositionDiagGuard::Answered()
    {
        return g_answered;
    }

    CompositionDiagGuard::Scope::Scope()  { t_reportDisabled = true; }
    CompositionDiagGuard::Scope::~Scope() { t_reportDisabled = false; }
}

// Test seam: reads DisableCompositionDiag the way Windows.UI.Xaml.dll does,
// through its own import slots, inside a Scope when `guarded` is non-zero.
// Returns the DWORD read, -1 when the key or the value is absent, -2 when
// Windows.UI.Xaml.dll cannot load, -3 when the guard could not be installed.
extern "C" int __stdcall DeckleTaskbarReadCompositionDiagSwitch(int guarded)
{
    using deckle::xamldiag::CompositionDiagGuard;

    HMODULE xaml = LoadLibraryExW(kXamlModule, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!xaml) return -2;
    if (!CompositionDiagGuard::Install()) return -3;

    auto open  = reinterpret_cast<decltype(&RegOpenKeyExW)>(*FindImportSlot(xaml, "RegOpenKeyExW"));
    auto query = reinterpret_cast<decltype(&RegQueryValueExW)>(*FindImportSlot(xaml, "RegQueryValueExW"));

    std::optional<CompositionDiagGuard::Scope> scope;
    if (guarded) scope.emplace();

    HKEY key = nullptr;
    if (open(HKEY_LOCAL_MACHINE, kDebugKey, 0, KEY_QUERY_VALUE, &key) != ERROR_SUCCESS) return -1;

    DWORD value = 0, type = 0, size = sizeof value;
    LSTATUS status = query(key, kValueName, nullptr, &type, reinterpret_cast<LPBYTE>(&value), &size);
    RegCloseKey(key);
    if (status != ERROR_SUCCESS || type != REG_DWORD) return -1;
    return static_cast<int>(value);
}
