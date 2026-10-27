using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Deckle.Transcription.Whisper;

// Reads the DLL names a Windows PE image imports, without loading it. When a
// catalog DLL fails to load with "module not found", Windows does not say
// which import it could not resolve; this reader lets the loader name it.
//
// Same walk as scripts/lib/pe-imports.psm1, which guards the bundle at
// publication: the import directory and the delay-load directory, mapped
// through the section table.
internal static class PeImports
{
    private const ushort Pe32Magic     = 0x10B;
    private const ushort Pe32PlusMagic = 0x20B;
    private const int ImportDirectoryIndex      = 1;
    private const int DelayImportDirectoryIndex = 13;

    // Throws BadImageFormatException for anything that is not a well-formed
    // image, a truncated file included: the walk reads offsets the file
    // itself declares, and a file cut short (an interrupted copy, a damaged
    // download) declares offsets past its end. Callers catch one type.
    public static IReadOnlyList<string> ReadImportedDllNames(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string name = Path.GetFileName(path);
        try
        {
            return Read(bytes, name, path);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
        {
            throw new BadImageFormatException($"{name} is truncated or malformed: {ex.Message}", path, ex);
        }
    }

    private static List<string> Read(byte[] bytes, string name, string path)
    {

        if (bytes.Length < 0x40 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
            throw new BadImageFormatException($"{name} is not a PE image: no MZ header", path);
        uint peOffset = BitConverter.ToUInt32(bytes, 0x3C);
        if (peOffset + 24 > bytes.Length || BitConverter.ToUInt32(bytes, (int)peOffset) != 0x00004550)
            throw new BadImageFormatException($"{name} is not a PE image: no PE signature", path);

        int coff = (int)peOffset + 4;
        ushort sectionCount = BitConverter.ToUInt16(bytes, coff + 2);
        ushort optionalSize = BitConverter.ToUInt16(bytes, coff + 16);
        int optional = coff + 20;
        ushort magic = BitConverter.ToUInt16(bytes, optional);
        ulong imageBase;
        int dirCountOffset, dirOffset;
        switch (magic)
        {
            case Pe32Magic:     imageBase = BitConverter.ToUInt32(bytes, optional + 28); dirCountOffset = 92;  dirOffset = 96;  break;
            case Pe32PlusMagic: imageBase = BitConverter.ToUInt64(bytes, optional + 24); dirCountOffset = 108; dirOffset = 112; break;
            default: throw new BadImageFormatException($"{name} has an unknown optional header magic 0x{magic:X4}", path);
        }
        uint sizeOfHeaders = BitConverter.ToUInt32(bytes, optional + 60);
        uint dirCount = BitConverter.ToUInt32(bytes, optional + dirCountOffset);

        var sections = new List<(uint VirtualAddress, uint Span, uint RawPointer)>(sectionCount);
        int sectionTable = optional + optionalSize;
        for (int i = 0; i < sectionCount; i++)
        {
            int s = sectionTable + i * 40;
            uint virtualSize = BitConverter.ToUInt32(bytes, s + 8);
            uint rawSize = BitConverter.ToUInt32(bytes, s + 16);
            sections.Add((BitConverter.ToUInt32(bytes, s + 12), Math.Max(virtualSize, rawSize), BitConverter.ToUInt32(bytes, s + 20)));
        }

        int ToFileOffset(uint rva)
        {
            foreach ((uint va, uint span, uint raw) in sections)
                if (rva >= va && rva < va + span) return (int)(rva - va + raw);
            // Data inside the headers is mapped as is.
            if (rva < sizeOfHeaders) return (int)rva;
            throw new BadImageFormatException($"{name} maps RVA 0x{rva:X8} outside every section", path);
        }

        uint? DirectoryRva(int index)
        {
            if (index >= dirCount) return null;
            int d = optional + dirOffset + index * 8;
            uint rva = BitConverter.ToUInt32(bytes, d);
            uint size = BitConverter.ToUInt32(bytes, d + 4);
            return rva == 0 || size == 0 ? null : rva;
        }

        string AsciiZ(int offset)
        {
            int end = offset;
            while (end < bytes.Length && bytes[end] != 0) end++;
            return Encoding.ASCII.GetString(bytes, offset, end - offset);
        }

        var names = new List<string>();

        if (DirectoryRva(ImportDirectoryIndex) is uint importRva)
        {
            int descriptor = ToFileOffset(importRva);
            while (descriptor + 20 <= bytes.Length)
            {
                uint originalFirstThunk = BitConverter.ToUInt32(bytes, descriptor);
                uint nameRva = BitConverter.ToUInt32(bytes, descriptor + 12);
                uint firstThunk = BitConverter.ToUInt32(bytes, descriptor + 16);
                if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0) break;
                if (nameRva != 0) names.Add(AsciiZ(ToFileOffset(nameRva)));
                descriptor += 20;
            }
        }

        if (DirectoryRva(DelayImportDirectoryIndex) is uint delayRva)
        {
            int descriptor = ToFileOffset(delayRva);
            while (descriptor + 32 <= bytes.Length)
            {
                uint attributes = BitConverter.ToUInt32(bytes, descriptor);
                uint nameField = BitConverter.ToUInt32(bytes, descriptor + 4);
                if (nameField == 0) break;
                // Bit 0 set: fields are RVAs (the current format). Clear:
                // virtual addresses, which the image base turns back into RVAs.
                uint nameRva = (attributes & 1) != 0 ? nameField : (uint)(nameField - imageBase);
                names.Add(AsciiZ(ToFileOffset(nameRva)));
                descriptor += 32;
            }
        }

        return names;
    }
}
