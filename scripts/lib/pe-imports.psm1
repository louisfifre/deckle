# Reads the DLL names a Windows PE image imports, and checks that a folder of
# DLLs can be loaded on a machine that has nothing but Windows. The loader on
# the tester's machine resolves every import by name, so a bundle whose DLLs
# import anything that is neither beside them nor part of Windows loads on
# the build machine (where PATH hides the gap) and fails everywhere else.
#
# The reader walks the PE headers by hand: the import directory and the
# delay-load directory, mapped through the section table. It never loads the
# image, so it can inspect a DLL built for any toolchain without running it.

$ErrorActionPreference = 'Stop'

# DLLs every supported Windows ships in System32, plus the API set names the
# universal CRT and the kernel expose. A name outside this list is only valid
# when it is in the bundle or declared external by the caller.
$script:WindowsSystemDllNames = @(
    'kernel32.dll', 'kernelbase.dll', 'ntdll.dll', 'user32.dll', 'gdi32.dll',
    'advapi32.dll', 'shell32.dll', 'ole32.dll', 'oleaut32.dll', 'shlwapi.dll',
    'ws2_32.dll', 'bcrypt.dll', 'crypt32.dll', 'msvcrt.dll', 'ucrtbase.dll',
    'dbghelp.dll', 'version.dll', 'winmm.dll', 'rpcrt4.dll', 'sechost.dll',
    'imagehlp.dll', 'psapi.dll', 'userenv.dll', 'iphlpapi.dll', 'setupapi.dll',
    'cfgmgr32.dll', 'powrprof.dll', 'd3d12.dll', 'dxgi.dll', 'dxcore.dll'
)

function Test-DeckleWindowsSystemDll {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)

    $lower = $Name.ToLowerInvariant()
    if ($script:WindowsSystemDllNames -contains $lower) { return $true }
    # API sets: api-ms-win-crt-*, api-ms-win-core-*, ext-ms-win-*.
    return $lower -match '^(api|ext)-ms-win-[a-z0-9-]+-l\d+-\d+-\d+\.dll$'
}

function Read-UInt16([byte[]]$Bytes, [int]$Offset) { [BitConverter]::ToUInt16($Bytes, $Offset) }
function Read-UInt32([byte[]]$Bytes, [int]$Offset) { [BitConverter]::ToUInt32($Bytes, $Offset) }
function Read-UInt64([byte[]]$Bytes, [int]$Offset) { [BitConverter]::ToUInt64($Bytes, $Offset) }

function Read-AsciiZ([byte[]]$Bytes, [int]$Offset) {
    $end = $Offset
    while ($end -lt $Bytes.Length -and $Bytes[$end] -ne 0) { $end++ }
    return [Text.Encoding]::ASCII.GetString($Bytes, $Offset, $end - $Offset)
}

function Get-DecklePeImports {
    <#
    .SYNOPSIS
        Reads the DLL names a PE image imports, static and delay-loaded.
    .OUTPUTS
        Path, Name, Machine (0x8664 for x64), Imports, DelayImports.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "PE image not found: $Path"
    }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $name = Split-Path -Leaf $Path

    if ($bytes.Length -lt 0x40 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "$name is not a PE image: no MZ header"
    }
    $peOffset = Read-UInt32 $bytes 0x3C
    if ($peOffset + 24 -gt $bytes.Length -or (Read-UInt32 $bytes $peOffset) -ne 0x00004550) {
        throw "$name is not a PE image: no PE signature"
    }

    $coff = $peOffset + 4
    $machine = Read-UInt16 $bytes $coff
    $sectionCount = Read-UInt16 $bytes ($coff + 2)
    $optionalSize = Read-UInt16 $bytes ($coff + 16)
    $optional = $coff + 20
    $magic = Read-UInt16 $bytes $optional
    switch ($magic) {
        0x10B { $imageBase = [uint64](Read-UInt32 $bytes ($optional + 28)); $dirCountOffset = 92; $dirOffset = 96 }
        0x20B { $imageBase = Read-UInt64 $bytes ($optional + 24); $dirCountOffset = 108; $dirOffset = 112 }
        default { throw "$name has an unknown optional header magic 0x$($magic.ToString('X4'))" }
    }
    $sizeOfHeaders = Read-UInt32 $bytes ($optional + 60)
    $dirCount = Read-UInt32 $bytes ($optional + $dirCountOffset)

    $sections = [System.Collections.Generic.List[object]]::new()
    $sectionTable = $optional + $optionalSize
    for ($i = 0; $i -lt $sectionCount; $i++) {
        $s = $sectionTable + $i * 40
        $virtualSize = Read-UInt32 $bytes ($s + 8)
        $rawSize = Read-UInt32 $bytes ($s + 16)
        $sections.Add([pscustomobject]@{
            VirtualAddress = Read-UInt32 $bytes ($s + 12)
            Span           = [Math]::Max($virtualSize, $rawSize)
            RawPointer     = Read-UInt32 $bytes ($s + 20)
        })
    }

    function ConvertTo-FileOffset([uint32]$rva) {
        foreach ($section in $sections) {
            if ($rva -ge $section.VirtualAddress -and $rva -lt $section.VirtualAddress + $section.Span) {
                return [int]($rva - $section.VirtualAddress + $section.RawPointer)
            }
        }
        # Data inside the headers is mapped as is.
        if ($rva -lt $sizeOfHeaders) { return [int]$rva }
        throw "$name maps RVA 0x$($rva.ToString('X8')) outside every section"
    }

    function Get-Directory([int]$index) {
        if ($index -ge $dirCount) { return $null }
        $d = $optional + $dirOffset + $index * 8
        $rva = Read-UInt32 $bytes $d
        $size = Read-UInt32 $bytes ($d + 4)
        if ($rva -eq 0 -or $size -eq 0) { return $null }
        return $rva
    }

    $imports = [System.Collections.Generic.List[string]]::new()
    $importRva = Get-Directory 1
    if ($null -ne $importRva) {
        $descriptor = ConvertTo-FileOffset $importRva
        while ($descriptor + 20 -le $bytes.Length) {
            $originalFirstThunk = Read-UInt32 $bytes $descriptor
            $nameRva = Read-UInt32 $bytes ($descriptor + 12)
            $firstThunk = Read-UInt32 $bytes ($descriptor + 16)
            if ($originalFirstThunk -eq 0 -and $nameRva -eq 0 -and $firstThunk -eq 0) { break }
            if ($nameRva -ne 0) {
                $imports.Add((Read-AsciiZ $bytes (ConvertTo-FileOffset $nameRva)))
            }
            $descriptor += 20
        }
    }

    $delayImports = [System.Collections.Generic.List[string]]::new()
    $delayRva = Get-Directory 13
    if ($null -ne $delayRva) {
        $descriptor = ConvertTo-FileOffset $delayRva
        while ($descriptor + 32 -le $bytes.Length) {
            $attributes = Read-UInt32 $bytes $descriptor
            $nameField = Read-UInt32 $bytes ($descriptor + 4)
            if ($nameField -eq 0) { break }
            # Bit 0 set: fields are RVAs (the current format). Clear: virtual
            # addresses, which the image base turns back into RVAs.
            $nameRva = if ($attributes -band 1) { $nameField } else { [uint32]($nameField - $imageBase) }
            $delayImports.Add((Read-AsciiZ $bytes (ConvertTo-FileOffset $nameRva)))
            $descriptor += 32
        }
    }

    return [pscustomobject]@{
        Path         = $Path
        Name         = $name
        Machine      = $machine
        Imports      = @($imports)
        DelayImports = @($delayImports)
    }
}

function Get-DeckleNativeImportGaps {
    <#
    .SYNOPSIS
        Lists every import of the given DLLs that a bare Windows machine
        cannot resolve from the folder itself.
    .DESCRIPTION
        Walks the import tables of every DLL named, recursing through the
        imports that are themselves in the folder. An import is resolved when
        it is one of the named DLLs, a Windows system DLL, or a name the caller
        declares external (vulkan-1.dll, which the graphics driver provides).
        A DLL present in the folder but absent from the names is reported as
        well: the catalog, not the folder, is what the installer extracts.
    .OUTPUTS
        One object per gap: Dll (the importer), Import (the name it needs),
        Reason ('missing' or 'uncatalogued').
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string[]]$DllNames,
        [string[]]$ExternalDllNames = @()
    )

    $catalog = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($n in $DllNames) { $null = $catalog.Add($n) }
    $external = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($n in $ExternalDllNames) { $null = $external.Add($n) }

    $gaps = [System.Collections.Generic.List[object]]::new()
    $visited = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue = [System.Collections.Generic.Queue[string]]::new()
    foreach ($n in $DllNames) { $queue.Enqueue($n) }

    while ($queue.Count) {
        $current = $queue.Dequeue()
        if (-not $visited.Add($current)) { continue }
        $path = Join-Path $Directory $current
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $gaps.Add([pscustomobject]@{ Dll = $current; Import = ''; Reason = 'absent' })
            continue
        }
        $image = Get-DecklePeImports -Path $path
        foreach ($import in @($image.Imports) + @($image.DelayImports)) {
            if ($external.Contains($import) -or (Test-DeckleWindowsSystemDll -Name $import)) { continue }
            if ($catalog.Contains($import)) { $queue.Enqueue($import); continue }
            $sibling = Join-Path $Directory $import
            $reason = if (Test-Path -LiteralPath $sibling -PathType Leaf) { 'uncatalogued' } else { 'missing' }
            $gaps.Add([pscustomobject]@{ Dll = $current; Import = $import; Reason = $reason })
        }
    }

    return @($gaps)
}

function Assert-DeckleNativeImportsResolved {
    <#
    .SYNOPSIS
        Throws when any catalog DLL imports something a bare Windows machine
        would not find beside it.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string[]]$DllNames,
        [string[]]$ExternalDllNames = @()
    )

    $gaps = Get-DeckleNativeImportGaps -Directory $Directory -DllNames $DllNames -ExternalDllNames $ExternalDllNames
    if (-not $gaps.Count) { return }

    $lines = foreach ($gap in $gaps) {
        switch ($gap.Reason) {
            'absent'       { "$($gap.Dll) is not in the bundle" }
            'uncatalogued' { "$($gap.Dll) imports $($gap.Import), which is beside it but not in the catalog" }
            default        { "$($gap.Dll) imports $($gap.Import), which is neither in the bundle nor part of Windows" }
        }
    }
    throw "Native runtime would not load on a bare Windows machine: $($lines -join '; ')"
}

Export-ModuleMember -Function Get-DecklePeImports, Test-DeckleWindowsSystemDll, Get-DeckleNativeImportGaps, Assert-DeckleNativeImportsResolved
