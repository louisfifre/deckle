using System.Text;

namespace Deckle.Transcription.Whisper.Tests;

// Builds minimal x64 DLL images with a chosen import table. The image carries
// no code: Windows loads it (no entry point, relocatable with nothing to
// relocate, one data section) and resolves its imports exactly as it would
// for a real DLL, so a fixture that imports an absent name fails to load the
// same way the tester's bundle did. Mirror of scripts/tests/lib/pe-fixture.ps1.
//
// Each import names one function, because the loader skips a module whose
// import address table is empty: KERNEL32.dll's GetLastError, and for any
// other DLL the symbol every fixture exports, DeckleFixtureExport.
internal static class TestPeImage
{
    private const uint SectionRva = 0x1000;
    private const string FixtureExport = "DeckleFixtureExport";

    public static void Write(string path, IReadOnlyList<string>? imports = null, IReadOnlyList<string>? delayImports = null)
    {
        imports ??= [];
        delayImports ??= [];

        int importTableSize = (imports.Count + 1) * 20;
        int delayTableSize = delayImports.Count > 0 ? (delayImports.Count + 1) * 32 : 0;
        int exportTableSize = 40;
        int dataStart = importTableSize + delayTableSize + exportTableSize;
        while (dataStart % 8 != 0) dataStart++;

        // Data block first, so every RVA is known when the tables are written.
        var data = new List<byte>();
        uint Here() => SectionRva + (uint)dataStart + (uint)data.Count;
        void Align8() { while (data.Count % 8 != 0) data.Add(0); }
        uint AddAsciiZ(string text)
        {
            uint rva = Here();
            data.AddRange(Encoding.ASCII.GetBytes(text));
            data.Add(0);
            Align8();
            return rva;
        }
        uint AddHintName(string function)
        {
            uint rva = Here();
            data.AddRange([0, 0]);
            data.AddRange(Encoding.ASCII.GetBytes(function));
            data.Add(0);
            Align8();
            return rva;
        }
        uint AddThunks(ulong first)
        {
            uint rva = Here();
            data.AddRange(BitConverter.GetBytes(first));
            data.AddRange(new byte[8]);
            return rva;
        }
        uint AddU32(uint value) { uint rva = Here(); data.AddRange(BitConverter.GetBytes(value)); Align8(); return rva; }
        uint AddU16(ushort value) { uint rva = Here(); data.AddRange(BitConverter.GetBytes(value)); Align8(); return rva; }

        static string FunctionFor(string dll) =>
            dll.Equals("KERNEL32.dll", StringComparison.OrdinalIgnoreCase) ? "GetLastError" : FixtureExport;

        var importEntries = imports.Select(dll =>
        {
            uint hintName = AddHintName(FunctionFor(dll));
            return (Lookup: AddThunks(hintName), Address: AddThunks(hintName), Name: AddAsciiZ(dll));
        }).ToList();
        var delayEntries = delayImports.Select(dll =>
        {
            uint hintName = AddHintName(FunctionFor(dll));
            return (Handle: AddThunks(0), Address: AddThunks(hintName), Lookup: AddThunks(hintName), Name: AddAsciiZ(dll));
        }).ToList();

        // One exported symbol, pointing at a data byte: never called, only
        // resolved by the fixtures that import it.
        uint exportTarget = AddU32(0);
        uint exportName = AddAsciiZ(FixtureExport);
        uint moduleName = AddAsciiZ(Path.GetFileName(path));
        uint functions = AddU32(exportTarget);
        uint names = AddU32(exportName);
        uint ordinals = AddU16(0);

        var body = new List<byte>();
        void U32(uint v) => body.AddRange(BitConverter.GetBytes(v));
        foreach (var e in importEntries)
        {
            U32(e.Lookup); U32(0); U32(0); U32(e.Name); U32(e.Address);
        }
        body.AddRange(new byte[20]);
        foreach (var e in delayEntries)
        {
            U32(1); U32(e.Name); U32(e.Handle); U32(e.Address); U32(e.Lookup); U32(0); U32(0); U32(0);
        }
        if (delayImports.Count > 0) body.AddRange(new byte[32]);
        uint exportDirectoryRva = SectionRva + (uint)body.Count;
        U32(0); U32(0); U32(0);                      // Characteristics, TimeDateStamp, versions
        U32(moduleName); U32(1); U32(1); U32(1);     // Name, Base, NumberOfFunctions, NumberOfNames
        U32(functions); U32(names); U32(ordinals);
        while (body.Count < dataStart) body.Add(0);
        body.AddRange(data);
        uint virtualSize = (uint)body.Count;
        while (body.Count % 0x200 != 0) body.Add(0);
        uint rawSize = (uint)body.Count;

        var headers = new List<byte>();
        void H16(ushort v) => headers.AddRange(BitConverter.GetBytes(v));
        void H32(uint v) => headers.AddRange(BitConverter.GetBytes(v));
        void H64(ulong v) => headers.AddRange(BitConverter.GetBytes(v));

        headers.AddRange([(byte)'M', (byte)'Z']);
        headers.AddRange(new byte[58]);
        H32(0x40);                                   // e_lfanew
        headers.AddRange([(byte)'P', (byte)'E', 0, 0]);
        H16(0x8664);                                 // Machine x64
        H16(1);                                      // NumberOfSections
        H32(0); H32(0); H32(0);                      // timestamp, symbols
        H16(240);                                    // SizeOfOptionalHeader
        H16(0x2022);                                 // EXECUTABLE_IMAGE | LARGE_ADDRESS_AWARE | DLL
        H16(0x20B);                                  // PE32+
        headers.AddRange([14, 0]);                   // linker version
        H32(0);                                      // SizeOfCode
        H32(rawSize);                                // SizeOfInitializedData
        H32(0);                                      // SizeOfUninitializedData
        H32(0);                                      // AddressOfEntryPoint: no DllMain
        H32(SectionRva);                             // BaseOfCode
        H64(0x180000000);                            // ImageBase
        H32(0x1000); H32(0x200);                     // section / file alignment
        H16(6); H16(0); H16(0); H16(0); H16(6); H16(0);
        H32(0);                                      // Win32VersionValue
        H32(0x2000);                                 // SizeOfImage
        H32(0x200);                                  // SizeOfHeaders
        H32(0);                                      // CheckSum
        H16(2);                                      // Subsystem: Windows GUI
        H16(0x160);                                  // HIGH_ENTROPY_VA | DYNAMIC_BASE | NX_COMPAT
        H64(0x100000); H64(0x1000); H64(0x100000); H64(0x1000);
        H32(0);                                      // LoaderFlags
        H32(16);                                     // NumberOfRvaAndSizes
        for (int i = 0; i < 16; i++)
        {
            if (i == 0) { H32(exportDirectoryRva); H32((uint)exportTableSize); }
            else if (i == 1) { H32(SectionRva); H32((uint)importTableSize); }
            else if (i == 13 && delayTableSize > 0) { H32(SectionRva + (uint)importTableSize); H32((uint)delayTableSize); }
            else { H32(0); H32(0); }
        }
        headers.AddRange(Encoding.ASCII.GetBytes(".idata"));
        headers.AddRange([0, 0]);
        H32(virtualSize);
        H32(SectionRva);
        H32(rawSize);
        H32(0x200);                                  // PointerToRawData
        H32(0); H32(0); H16(0); H16(0);
        H32(0xC0000040);                             // INITIALIZED_DATA | READ | WRITE
        while (headers.Count < 0x200) headers.Add(0);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [.. headers, .. body]);
    }
}
