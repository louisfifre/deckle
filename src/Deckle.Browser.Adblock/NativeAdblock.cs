using System.Runtime.InteropServices;

namespace Deckle.Browser.Adblock;

// The C ABI of deckle_adblock.dll (native/Deckle.Browser.Adblock/src/lib.rs, whose header
// states the contract). Strings go in as UTF-8 bytes with a length and no terminator; the
// one buffer that comes out (the cosmetic JSON) is returned to the DLL with its length.
// The engine is not thread-safe: AdblockEngine serialises the calls.
internal static unsafe partial class NativeAdblock
{
    private const string Dll = "deckle_adblock";

    [LibraryImport(Dll, EntryPoint = "adblock_engine_new")]
    internal static partial nint EngineNew(byte* rules, nuint rulesLength);

    [LibraryImport(Dll, EntryPoint = "adblock_engine_new_trusted")]
    internal static partial nint EngineNewTrusted(byte* rules, nuint rulesLength, byte* trusted, nuint trustedLength);

    [LibraryImport(Dll, EntryPoint = "adblock_engine_free")]
    internal static partial void EngineFree(nint engine);

    [LibraryImport(Dll, EntryPoint = "adblock_should_block")]
    internal static partial byte ShouldBlock(nint engine, byte* url, nuint urlLength, byte* source, nuint sourceLength, byte* type, nuint typeLength);

    [LibraryImport(Dll, EntryPoint = "adblock_use_resources")]
    internal static partial byte UseResources(nint engine, byte* json, nuint length);

    [LibraryImport(Dll, EntryPoint = "adblock_cosmetic")]
    internal static partial byte* Cosmetic(nint engine, byte* url, nuint urlLength, nuint* outLength);

    [LibraryImport(Dll, EntryPoint = "adblock_string_free")]
    internal static partial void StringFree(byte* pointer, nuint length);
}
