#pragma once

#include <array>
#include <string>

namespace deckle::patches
{
    // A function of a module explorer has loaded, as Deckle resolved it: the C#
    // side reads the module's file and Microsoft's public symbols
    // (ExplorerFunctionLocator) and sends where the function is.
    //
    //   "id": "bar.thickness", "module": "Taskbar.View.dll", "timestamp": 1789085818,
    //   "imageSize": 10055680, "rva": 283120, "original": "4883EC2885C97438..."
    //
    // timestamp and imageSize are the module's TimeDateStamp and SizeOfImage as
    // Deckle read them on disk; "original" is the function's first 16 bytes, in
    // hex, from the same file. A patch applies only when the module in memory is
    // that file and the bytes are those: another build of Windows, or another
    // tool's patch, is left alone.
    struct FunctionSite
    {
        static constexpr size_t PrologueLength = 16;

        std::wstring id;
        std::wstring module;
        uint32_t timestamp = 0;
        uint32_t imageSize = 0;
        uint32_t rva = 0;
        std::array<uint8_t, PrologueLength> original{};
    };

    // Reads the fields above from one entry of the document. False when one is
    // missing or unreadable.
    bool ReadSite(winrt::Windows::Data::Json::JsonObject const& entry, FunctionSite& site);

    // The function's address in memory, once the module there is the file Deckle
    // read and the address is the start of a function at least 16 bytes long.
    // Null with the reason (snake_case, for the log) otherwise.
    uint8_t* Locate(FunctionSite const& site, std::wstring& reason);

    // A number of a document entry; false when the key is absent or not a number.
    bool ReadNumber(winrt::Windows::Data::Json::JsonObject const& entry, std::wstring_view key, double& value);
}
