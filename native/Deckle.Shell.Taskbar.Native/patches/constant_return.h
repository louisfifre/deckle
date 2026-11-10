#pragma once

#include <array>
#include <string>
#include <vector>

namespace deckle::patches
{
    // Makes a function of explorer return a constant: its first 16 bytes become
    //   mov rax, <value as a double>; movq xmm0, rax; ret
    // so whoever calls it gets the value at once, and the rest of its body is
    // never reached. Nothing jumps into this DLL: the patch holds no address of
    // ours, so the DLL can unload while it is in place, and turning it off
    // writes the original bytes back.
    //
    // It only fits a function that returns a double and needs no side effect,
    // such as TaskbarConfiguration::GetFrameSize, which answers the taskbar's
    // thickness. Which function, at which address, is Deckle's business: the
    // C# side resolves it from Microsoft's public symbols and sends it here.
    //
    //   "constantReturns": [ { "id": "bar.thickness", "module": "Taskbar.View.dll",
    //                          "timestamp": 1789085818, "imageSize": 10055680,
    //                          "rva": 283120, "original": "4883EC2885C97438...", "value": 32 } ]
    //
    // timestamp and imageSize are the module's TimeDateStamp and SizeOfImage as
    // Deckle read them on disk; "original" is the function's first 16 bytes, in
    // hex, from the same file. The patch applies only when the module in memory
    // is that file and the bytes are those (or a patch of ours left by an earlier
    // engine): another build of Windows, or another tool's patch, is left alone.
    struct ConstantReturn
    {
        std::wstring id;
        std::wstring module;
        uint32_t timestamp = 0;
        uint32_t imageSize = 0;
        uint32_t rva = 0;
        std::array<uint8_t, 16> original{};
        double value = 0;
    };

    // The "constantReturns" array of a settings document; an entry that cannot
    // be read is named in problems and left out.
    std::vector<ConstantReturn> Parse(winrt::Windows::Data::Json::JsonObject const& document,
                                      std::vector<std::wstring>& problems);

    // Makes the set the one in force: a function patched before and absent from
    // it gets its bytes back, each entry returns its value. Logs one record per
    // entry. True when some function's code changed. The worker thread.
    bool Apply(std::vector<ConstantReturn> const& set);

    // Gives every patched function its bytes back. Returns how many.
    size_t RestoreAll();
}
