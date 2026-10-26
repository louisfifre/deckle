$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'pe-imports.psm1') -Force
. (Join-Path $PSScriptRoot 'pe-fixture.ps1')

function Assert-Throws([scriptblock]$Action, [string]$MessageFragment) {
    try {
        & $Action
        throw "Expected failure containing '$MessageFragment'"
    } catch {
        if ($_.Exception.Message -notlike "*$MessageFragment*") { throw }
    }
}

$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-pe-imports-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path $root

    # The reader returns the static and delay-loaded imports of a PE32+ image.
    $image = Join-Path $root 'sample.dll'
    New-DeckleTestPeImage -Path $image -Imports @('KERNEL32.dll', 'libgomp-1.dll') -DelayImports @('vulkan-1.dll')
    $read = Get-DecklePeImports -Path $image
    if ($read.Machine -ne 0x8664) { throw 'Fixture machine should be x64' }
    if (($read.Imports -join ',') -cne 'KERNEL32.dll,libgomp-1.dll') { throw "Static imports were not read: $($read.Imports -join ',')" }
    if (($read.DelayImports -join ',') -cne 'vulkan-1.dll') { throw "Delay imports were not read: $($read.DelayImports -join ',')" }

    # An image without imports reads as empty, not as an error.
    $bare = Join-Path $root 'bare.dll'
    New-DeckleTestPeImage -Path $bare
    $readBare = Get-DecklePeImports -Path $bare
    if ($readBare.Imports.Count -or $readBare.DelayImports.Count) { throw 'An import-free image should read as empty' }

    # A file that is not a PE image is refused by name.
    $text = Join-Path $root 'text.dll'
    Set-Content -LiteralPath $text -Value 'fixture text.dll'
    Assert-Throws { Get-DecklePeImports -Path $text } 'not a PE image'

    # The fixture reads like a real DLL: Windows loads it and resolves its
    # imports, so the import table shape is the one the loader expects, and a
    # fixture importing an absent DLL fails the way a real one does.
    $loadable = Join-Path $root 'loadable.dll'
    New-DeckleTestPeImage -Path $loadable -Imports @('KERNEL32.dll', 'bare.dll')
    $loaded = [System.Runtime.InteropServices.NativeLibrary]::Load($loadable)
    if ($loaded -eq [IntPtr]::Zero) { throw 'Windows should load a fixture whose imports resolve' }
    [System.Runtime.InteropServices.NativeLibrary]::Free($loaded)
    $leaky = Join-Path $root 'leaky.dll'
    New-DeckleTestPeImage -Path $leaky -Imports @('KERNEL32.dll', 'deckle-absent-dependency.dll')
    $handle = [IntPtr]::Zero
    if ([System.Runtime.InteropServices.NativeLibrary]::TryLoad($leaky, [ref]$handle)) {
        throw 'Windows must refuse a fixture importing an absent DLL'
    }

    # Sanity against a real system DLL: kernel32 imports ntdll.
    $kernel32 = Get-DecklePeImports -Path (Join-Path $env:SystemRoot 'System32\kernel32.dll')
    if (-not ($kernel32.Imports | Where-Object { $_ -ieq 'ntdll.dll' })) { throw 'kernel32.dll should import ntdll.dll' }

    # System DLL recognition: the kernel, the CRT API sets, nothing else.
    foreach ($name in @('KERNEL32.dll', 'api-ms-win-crt-math-l1-1-0.dll', 'ADVAPI32.dll')) {
        if (-not (Test-DeckleWindowsSystemDll -Name $name)) { throw "$name should count as a Windows system DLL" }
    }
    foreach ($name in @('vcruntime140.dll', 'libgomp-1.dll', 'vulkan-1.dll', 'msvcp140.dll')) {
        if (Test-DeckleWindowsSystemDll -Name $name) { throw "$name must not count as a Windows system DLL" }
    }

    # Gap analysis over a folder: a imports b (catalogued), libgomp (absent),
    # kernel32 (Windows) and vulkan-1 (declared external); b imports a CRT
    # API set and c, which is beside it but not in the catalog.
    $bundle = Join-Path $root 'bundle'
    New-DeckleTestPeImage -Path (Join-Path $bundle 'a.dll') -Imports @('b.dll', 'libgomp-1.dll', 'KERNEL32.dll') -DelayImports @('vulkan-1.dll')
    New-DeckleTestPeImage -Path (Join-Path $bundle 'b.dll') -Imports @('api-ms-win-crt-runtime-l1-1-0.dll', 'c.dll')
    New-DeckleTestPeImage -Path (Join-Path $bundle 'c.dll')
    $gaps = @(Get-DeckleNativeImportGaps -Directory $bundle -DllNames @('a.dll', 'b.dll') -ExternalDllNames @('vulkan-1.dll'))
    $gapLines = @($gaps | ForEach-Object { "$($_.Dll)>$($_.Import):$($_.Reason)" })
    if (($gapLines -join ',') -cne 'a.dll>libgomp-1.dll:missing,b.dll>c.dll:uncatalogued') {
        throw "Unexpected import gaps: $($gapLines -join ',')"
    }
    Assert-Throws {
        Assert-DeckleNativeImportsResolved -Directory $bundle -DllNames @('a.dll', 'b.dll') -ExternalDllNames @('vulkan-1.dll')
    } 'a.dll imports libgomp-1.dll, which is neither in the bundle nor part of Windows'
    Assert-Throws {
        Assert-DeckleNativeImportsResolved -Directory $bundle -DllNames @('a.dll', 'b.dll') -ExternalDllNames @('vulkan-1.dll')
    } 'b.dll imports c.dll, which is beside it but not in the catalog'

    # Without the external declaration, vulkan-1 is a gap too.
    $vulkanGap = @(Get-DeckleNativeImportGaps -Directory $bundle -DllNames @('a.dll', 'b.dll', 'c.dll') | Where-Object Import -ieq 'vulkan-1.dll')
    if ($vulkanGap.Count -ne 1) { throw 'An undeclared vulkan-1.dll import should be a gap' }

    # A catalog name with no file is an absent DLL.
    $absent = @(Get-DeckleNativeImportGaps -Directory $bundle -DllNames @('c.dll', 'd.dll') | Where-Object Reason -ceq 'absent')
    if ($absent.Count -ne 1 -or $absent[0].Dll -cne 'd.dll') { throw 'A catalogued DLL missing from the folder should be reported absent' }

    # Once libgomp is beside the others and c is catalogued, the bundle resolves.
    New-DeckleTestPeImage -Path (Join-Path $bundle 'libgomp-1.dll') -Imports @('KERNEL32.dll')
    Assert-DeckleNativeImportsResolved -Directory $bundle -DllNames @('a.dll', 'b.dll', 'c.dll', 'libgomp-1.dll') -ExternalDllNames @('vulkan-1.dll')

    Write-Host 'pe-imports.tests.ps1 passed' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
