using System.Collections.Generic;
using System.Linq;

namespace Deckle.Transcription.Whisper;

// What loading one catalog DLL on this machine gave. Win32Error is 0 when it
// loaded; otherwise the loader's code (126 an import is missing, 193 not a
// 64-bit DLL, 1114 its initialization failed) and MissingImport the first
// import that is neither beside the DLL nor in System32, when it could be
// told. Summary is the sentence the exception and the wizard show.
public sealed record NativeDllLoadResult(
    string Name,
    bool Loaded,
    int Win32Error,
    string? MissingImport,
    string Summary);

// The answer to "does the installed runtime load here?", one entry per
// catalog DLL in load order. The Vulkan backend is the one DLL allowed to
// fail: it needs the Vulkan loader the graphics driver installs, and without
// it whisper.cpp runs on the CPU.
public sealed record NativeRuntimeLoadCheck(
    bool Loads,
    bool VulkanAvailable,
    IReadOnlyList<NativeDllLoadResult> Dlls)
{
    // The failure to show first: the earliest DLL that did not load, the
    // Vulkan backend excepted.
    public NativeDllLoadResult? FirstFailure =>
        Dlls.FirstOrDefault(d => !d.Loaded && !NativeRuntime.IsVulkanBackend(d.Name));
}
