<#
.SYNOPSIS
    Clean the native folder
.DESCRIPTION
    Brings the whisper.cpp native folder under the user data root back to the
    current catalog, the RequiredDllNames of NativeRuntime.cs: deletes every
    DLL the catalog does not name (an older bundle's libwhisper.dll and MinGW
    runtime, a stray build artifact) and the .partial leftovers of an
    interrupted extraction, then reports the catalog DLLs that are missing.
    The app does the same deletion on every provisioning; this script is for
    a developer folder filled by hand or by setup-assets.ps1. Deckle is not
    stopped: a DLL it holds open stays and is reported, close Deckle and run
    again. Missing DLLs are reported, never fetched: setup-assets.ps1 or the
    setup wizard provisions them.
.NOTES
    section: Cleanup
    order: 230
    action: Inspect native folder | -WhatIf
    action: Clean native folder | | danger | Deletes the DLLs the current native catalog does not name from the user data native folder.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    # Override of the target UserDataRoot. Highest priority, ahead of
    # DECKLE_DATA_ROOT and %LOCALAPPDATA%\Deckle\.
    [string]$DataRoot
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptDir)) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'native-runtime-release.psm1') -Force

$Repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $ScriptDir))
$Workflow = 'Clean native folder'
$NativeDir = $null
$removed = @()
$kept = @()
$missing = @()

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Native folder cleanup failed before completion." `
        -Details ([ordered]@{
            'Native folder' = $NativeDir
            Removed         = $(if ($removed.Count) { $removed } else { 'None' })
            Error           = $_.Exception.Message
        })
    throw
}

# Same order as AppPaths.ResolveUserDataRoot, after the -DataRoot override.
$TargetRoot = if ($DataRoot) { $DataRoot }
              elseif ($env:DECKLE_DATA_ROOT) { $env:DECKLE_DATA_ROOT }
              else { Join-Path $env:LOCALAPPDATA 'Deckle' }
$NativeDir = Join-Path $TargetRoot 'native'

# The catalog is read from this checkout's NativeRuntime.cs, so a branch that
# changes the bundle cleans to that branch's catalog.
$catalog = Get-DeckleNativeRuntimeCatalog -SourcePath (Join-Path $Repo 'src\Deckle.Transcription.Whisper\Setup\NativeRuntime.cs')
$names = [Collections.Generic.HashSet[string]]::new([string[]]$catalog.Names, [StringComparer]::OrdinalIgnoreCase)

Write-Host "Native folder: $NativeDir" -ForegroundColor DarkGray

if (Test-Path -LiteralPath $NativeDir -PathType Container) {
    # "*.dll" also matches "x.dllx" (the 8.3 rule), so the extension is
    # checked again; .partial files are half-written extractions.
    $stale = @(Get-ChildItem -LiteralPath $NativeDir -File -Force | Where-Object {
        ($_.Extension -ieq '.dll' -and -not $names.Contains($_.Name)) -or $_.Name -like '*.dll.partial'
    } | Sort-Object Name)

    foreach ($file in $stale) {
        if (-not $PSCmdlet.ShouldProcess($file.Name, 'Delete')) { continue }
        try {
            Remove-Item -LiteralPath $file.FullName -Force
            $removed += $file.Name
            Write-Host "  - $($file.Name)" -ForegroundColor DarkGray
        } catch [System.IO.IOException], [System.UnauthorizedAccessException] {
            $kept += $file.Name
            Write-Host "  ! $($file.Name) is in use: close Deckle and run again." -ForegroundColor Yellow
        }
    }
    $wouldRemove = if ($WhatIfPreference) { @($stale.Name) } else { @() }
} else {
    $stale = @()
    $wouldRemove = @()
}

# -WhatIf has done its part on the deletions; left on, it would also print
# a line for every pipeline cmdlet the summary runs.
$Inspect = [bool]$WhatIfPreference
$WhatIfPreference = $false

foreach ($name in $catalog.Names) {
    if (-not (Test-Path -LiteralPath (Join-Path $NativeDir $name) -PathType Leaf)) { $missing += $name }
}
foreach ($name in $missing) {
    Write-Host "  ? missing $name" -ForegroundColor Yellow
}

$result = if ($kept.Count -or $missing.Count -or ($Inspect -and $stale.Count)) { 'Partial' } else { 'Success' }
$sentence = if ($Inspect) {
    "Inspection found $($stale.Count) file(s) outside the catalog and $($missing.Count) catalog DLL(s) missing; nothing was deleted."
} elseif ($kept.Count) {
    "Native folder cleanup removed $($removed.Count) file(s); $($kept.Count) in use stay until Deckle is closed."
} elseif ($missing.Count) {
    "Native folder holds only catalog DLLs, but $($missing.Count) of them are missing."
} else {
    "Native folder holds exactly the current catalog."
}

Write-ActionSummary `
    -Workflow $Workflow `
    -Result $result `
    -Sentence $sentence `
    -Details ([ordered]@{
        'Native folder'  = $NativeDir
        'Catalog'        = "$($catalog.Names.Count) DLL(s) from NativeRuntime.cs"
        Removed          = $(if ($removed.Count) { $removed } else { 'None' })
        'Would remove'   = $(if ($wouldRemove.Count) { $wouldRemove } else { 'None' })
        'In use, kept'   = $(if ($kept.Count) { $kept } else { 'None' })
        Missing          = $(if ($missing.Count) { $missing } else { 'None' })
    }) `
    -Next $(if ($missing.Count) { @('Run scripts\commands\setup\setup-assets.ps1, or the setup wizard, to provision the missing DLLs.') } else { @() })
