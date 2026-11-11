#pragma once

#include "function_site.h"

#include <optional>
#include <vector>

namespace deckle::patches
{
    // Grows the thumbnails the taskbar shows when the pointer rests on an app.
    //
    // Explorer sizes each one in ThumbnailHelpers::GetScaledThumbnailSize(Size,
    // float): the window's size is shrunk, ratio kept, to fit a box of 200 x 110
    // effective pixels, never enlarged, then multiplied by the display's scale.
    // The box is two constants in the function's code, so no property of the
    // visual tree can widen it.
    //
    // The function is 117 bytes of arithmetic with no side effect, so it is
    // replaced whole: its first bytes become
    //   mov rax, <address of ours>; jmp rax
    // and ours does the same arithmetic with Deckle's box. Nothing of the
    // original runs any more, so none of its instructions has to be moved, and
    // turning the setting off writes the original bytes back.
    //
    // Unlike a constant return, this patch holds an address of this DLL: it is
    // taken out before the DLL parks or unloads, at a moment when no thread is
    // inside the DLL.
    //
    //   "thumbnailBox": { "id": "thumbnails.size", "module": "Taskbar.View.dll",
    //                     "timestamp": 1789085818, "imageSize": 10055680, "rva": 1388592,
    //                     "original": "4883EC18F30F102D...", "maxWidth": 640, "maxHeight": 330 }
    struct ThumbnailBox
    {
        FunctionSite site;
        double maxWidth = 0;
        double maxHeight = 0;
    };

    // The "thumbnailBox" object of a settings document, when it has one. An
    // object that cannot be read is named in problems and left out.
    std::optional<ThumbnailBox> ParseThumbnailBox(winrt::Windows::Data::Json::JsonObject const& document,
                                                  std::vector<std::wstring>& problems);

    // Makes the box the one in force; none gives the function its bytes back.
    // Logs one record. The worker thread.
    void ApplyThumbnailBox(std::optional<ThumbnailBox> const& box);

    // Gives the function its bytes back. True when it was patched and is not any more.
    bool RestoreThumbnailBox();

    // Whether the function still jumps into this DLL: a restore that found no
    // quiet moment leaves it so, and the DLL must then stay mapped.
    bool ThumbnailBoxInPlace();
}
