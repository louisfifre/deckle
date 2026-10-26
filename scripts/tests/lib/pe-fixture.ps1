# Builds minimal x64 DLL images with a chosen import table, for tests of the
# PE import reader and of the native runtime bundle checks. The image carries
# no code: Windows can load it (no entry point, relocatable with nothing to
# relocate, one data section) and resolves its imports exactly as it would
# for a real DLL, so a fixture that imports an absent name fails to load the
# same way. Mirror of tests/Deckle.Transcription.Whisper.Tests/TestPeImage.cs.
#
# Each import names one function, because the loader skips a module whose
# import address table is empty: KERNEL32.dll's GetLastError, and for any
# other DLL the symbol every fixture exports, DeckleFixtureExport.

$ErrorActionPreference = 'Stop'

function New-DeckleTestPeImage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [string[]]$Imports = @(),
        [string[]]$DelayImports = @()
    )

    $sectionRva = [uint32]0x1000
    $fixtureExport = 'DeckleFixtureExport'

    $importTableSize = ($Imports.Count + 1) * 20
    $delayTableSize = if ($DelayImports.Count) { ($DelayImports.Count + 1) * 32 } else { 0 }
    $exportTableSize = 40
    $dataStart = $importTableSize + $delayTableSize + $exportTableSize
    while ($dataStart % 8) { $dataStart++ }

    # Data block first, so every RVA is known when the tables are written.
    $data = [System.Collections.Generic.List[byte]]::new()
    function Get-Here { return [uint32]($sectionRva + $dataStart + $data.Count) }
    function Add-DataBytes([byte[]]$bytes) { foreach ($b in $bytes) { $data.Add($b) } }
    function Add-DataAlign { while ($data.Count % 8) { $data.Add(0) } }
    function Add-DataAsciiZ([string]$text) {
        $rva = Get-Here
        Add-DataBytes ([Text.Encoding]::ASCII.GetBytes($text))
        $data.Add(0)
        Add-DataAlign
        return $rva
    }
    function Add-DataHintName([string]$function) {
        $rva = Get-Here
        Add-DataBytes ([byte[]](0, 0))
        Add-DataBytes ([Text.Encoding]::ASCII.GetBytes($function))
        $data.Add(0)
        Add-DataAlign
        return $rva
    }
    function Add-DataThunks([uint64]$first) {
        $rva = Get-Here
        Add-DataBytes ([BitConverter]::GetBytes($first))
        Add-DataBytes (New-Object byte[] 8)
        return $rva
    }
    function Add-DataU32([uint32]$value) { $rva = Get-Here; Add-DataBytes ([BitConverter]::GetBytes($value)); Add-DataAlign; return $rva }
    function Add-DataU16([uint16]$value) { $rva = Get-Here; Add-DataBytes ([BitConverter]::GetBytes($value)); Add-DataAlign; return $rva }
    function Get-FunctionFor([string]$dll) {
        if ($dll -ieq 'KERNEL32.dll') { return 'GetLastError' }
        return $fixtureExport
    }

    $importEntries = foreach ($dll in $Imports) {
        $hintName = Add-DataHintName (Get-FunctionFor $dll)
        [pscustomobject]@{ Lookup = Add-DataThunks $hintName; Address = Add-DataThunks $hintName; Name = Add-DataAsciiZ $dll }
    }
    $delayEntries = foreach ($dll in $DelayImports) {
        $hintName = Add-DataHintName (Get-FunctionFor $dll)
        [pscustomobject]@{ Handle = Add-DataThunks 0; Address = Add-DataThunks $hintName; Lookup = Add-DataThunks $hintName; Name = Add-DataAsciiZ $dll }
    }

    # One exported symbol, pointing at a data byte: never called, only
    # resolved by the fixtures that import it.
    $exportTarget = Add-DataU32 0
    $exportName = Add-DataAsciiZ $fixtureExport
    $moduleName = Add-DataAsciiZ (Split-Path -Leaf $Path)
    $functions = Add-DataU32 $exportTarget
    $names = Add-DataU32 $exportName
    $ordinals = Add-DataU16 0

    $body = [System.Collections.Generic.List[byte]]::new()
    function Add-Bytes([byte[]]$bytes) { foreach ($b in $bytes) { $body.Add($b) } }
    function Add-UInt32([uint32]$value) { Add-Bytes ([BitConverter]::GetBytes($value)) }

    foreach ($entry in @($importEntries)) {
        Add-UInt32 $entry.Lookup   # OriginalFirstThunk
        Add-UInt32 0               # TimeDateStamp
        Add-UInt32 0               # ForwarderChain
        Add-UInt32 $entry.Name     # Name
        Add-UInt32 $entry.Address  # FirstThunk
    }
    Add-Bytes (New-Object byte[] 20)
    foreach ($entry in @($delayEntries)) {
        Add-UInt32 1               # Attributes: RVA fields
        Add-UInt32 $entry.Name
        Add-UInt32 $entry.Handle
        Add-UInt32 $entry.Address
        Add-UInt32 $entry.Lookup
        Add-UInt32 0               # BoundImportAddressTable
        Add-UInt32 0               # UnloadInformationTable
        Add-UInt32 0               # TimeDateStamp
    }
    if ($DelayImports.Count) { Add-Bytes (New-Object byte[] 32) }
    $exportDirectoryRva = [uint32]($sectionRva + $body.Count)
    Add-UInt32 0; Add-UInt32 0; Add-UInt32 0                      # Characteristics, TimeDateStamp, versions
    Add-UInt32 $moduleName; Add-UInt32 1; Add-UInt32 1; Add-UInt32 1   # Name, Base, NumberOfFunctions, NumberOfNames
    Add-UInt32 $functions; Add-UInt32 $names; Add-UInt32 $ordinals
    while ($body.Count -lt $dataStart) { $body.Add(0) }
    Add-Bytes $data.ToArray()
    $virtualSize = $body.Count
    while ($body.Count % 0x200) { $body.Add(0) }
    $rawSize = $body.Count

    # No code means no address to fix up: the image is marked relocatable
    # with an empty relocation directory, so the loader places it anywhere.
    $imageBase = [uint64]0x180000000

    $headers = [System.Collections.Generic.List[byte]]::new()
    function Add-HeaderBytes([byte[]]$bytes) { foreach ($b in $bytes) { $headers.Add($b) } }
    function Add-HeaderUInt16([uint16]$value) { Add-HeaderBytes ([BitConverter]::GetBytes($value)) }
    function Add-HeaderUInt32([uint32]$value) { Add-HeaderBytes ([BitConverter]::GetBytes($value)) }
    function Add-HeaderUInt64([uint64]$value) { Add-HeaderBytes ([BitConverter]::GetBytes($value)) }

    # DOS header: MZ, then e_lfanew = 0x40.
    Add-HeaderBytes ([byte[]](0x4D, 0x5A))
    Add-HeaderBytes (New-Object byte[] 58)
    Add-HeaderUInt32 0x40
    # PE signature and COFF header.
    Add-HeaderBytes ([byte[]](0x50, 0x45, 0, 0))
    Add-HeaderUInt16 0x8664        # Machine x64
    Add-HeaderUInt16 1             # NumberOfSections
    Add-HeaderUInt32 0             # TimeDateStamp
    Add-HeaderUInt32 0             # PointerToSymbolTable
    Add-HeaderUInt32 0             # NumberOfSymbols
    Add-HeaderUInt16 240           # SizeOfOptionalHeader
    Add-HeaderUInt16 0x2022        # EXECUTABLE_IMAGE | LARGE_ADDRESS_AWARE | DLL
    # Optional header, PE32+.
    Add-HeaderUInt16 0x20B
    Add-HeaderBytes ([byte[]](14, 0))  # linker version
    Add-HeaderUInt32 0             # SizeOfCode
    Add-HeaderUInt32 $rawSize      # SizeOfInitializedData
    Add-HeaderUInt32 0             # SizeOfUninitializedData
    Add-HeaderUInt32 0             # AddressOfEntryPoint: no DllMain
    Add-HeaderUInt32 $sectionRva   # BaseOfCode
    Add-HeaderUInt64 $imageBase
    Add-HeaderUInt32 0x1000        # SectionAlignment
    Add-HeaderUInt32 0x200         # FileAlignment
    Add-HeaderUInt16 6; Add-HeaderUInt16 0   # OS version
    Add-HeaderUInt16 0; Add-HeaderUInt16 0   # image version
    Add-HeaderUInt16 6; Add-HeaderUInt16 0   # subsystem version
    Add-HeaderUInt32 0             # Win32VersionValue
    Add-HeaderUInt32 0x2000        # SizeOfImage
    Add-HeaderUInt32 0x200         # SizeOfHeaders
    Add-HeaderUInt32 0             # CheckSum
    Add-HeaderUInt16 2             # Subsystem: Windows GUI
    Add-HeaderUInt16 0x160         # DllCharacteristics: HIGH_ENTROPY_VA | DYNAMIC_BASE | NX_COMPAT
    Add-HeaderUInt64 0x100000; Add-HeaderUInt64 0x1000   # stack
    Add-HeaderUInt64 0x100000; Add-HeaderUInt64 0x1000   # heap
    Add-HeaderUInt32 0             # LoaderFlags
    Add-HeaderUInt32 16            # NumberOfRvaAndSizes
    for ($i = 0; $i -lt 16; $i++) {
        switch ($i) {
            0  { Add-HeaderUInt32 $exportDirectoryRva; Add-HeaderUInt32 $exportTableSize }
            1  { Add-HeaderUInt32 $sectionRva; Add-HeaderUInt32 $importTableSize }
            13 {
                if ($delayTableSize) { Add-HeaderUInt32 ($sectionRva + $importTableSize); Add-HeaderUInt32 $delayTableSize }
                else { Add-HeaderUInt32 0; Add-HeaderUInt32 0 }
            }
            default { Add-HeaderUInt32 0; Add-HeaderUInt32 0 }
        }
    }
    # Section header.
    Add-HeaderBytes ([Text.Encoding]::ASCII.GetBytes('.idata'))
    Add-HeaderBytes ([byte[]](0, 0))
    Add-HeaderUInt32 $virtualSize
    Add-HeaderUInt32 $sectionRva
    Add-HeaderUInt32 $rawSize
    Add-HeaderUInt32 0x200         # PointerToRawData
    Add-HeaderUInt32 0; Add-HeaderUInt32 0; Add-HeaderUInt16 0; Add-HeaderUInt16 0
    Add-HeaderUInt32 ([uint32]3221225536)   # 0xC0000040: INITIALIZED_DATA | READ | WRITE
    while ($headers.Count -lt 0x200) { $headers.Add(0) }

    $image = [byte[]]($headers.ToArray() + $body.ToArray())
    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        $null = New-Item -ItemType Directory -Path $directory
    }
    [IO.File]::WriteAllBytes($Path, $image)
}
