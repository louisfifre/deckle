# deckle-preview.ps1 - Parallel, preview-only terminal interaction launcher.
#
# The stable scripts/deckle.ps1 launcher remains the daily workflow entry point.
# This preview exercises the replacement interaction model without invoking any
# repository command. Use snapshot mode to inspect deterministic layouts.

[CmdletBinding()]
param(
    [ValidateSet('Menu', 'Project', 'Preparation', 'Execution', 'Catalog')][string]$Snapshot,
    [ValidateRange(20, 240)][int]$Width = 100,
    [ValidateRange(8, 100)][int]$Height = 30,
    [switch]$HostSmokeTest,
    [switch]$Discovered
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path $ScriptDir 'lib'
$RepositoryRoot = Split-Path -Parent $ScriptDir
$PreviewBanner = 'Deckle Interaction Preview'

Import-Module (Join-Path $LibDir 'terminal-interaction.psm1') -Force
Import-Module (Join-Path $LibDir 'script-discovery.psm1') -Force
. (Join-Path $LibDir 'deckle-preview\catalog.ps1')
. (Join-Path $LibDir 'deckle-preview\discovered-catalog.ps1')
. (Join-Path $LibDir 'deckle-preview\statistics-preparation.ps1')
. (Join-Path $LibDir 'deckle-preview\flows.ps1')

# The discovered catalog is read only when asked for, so the hand-written
# preview keeps its exact behavior and cost.
$DiscoveredCatalog = $null
if ($Discovered -or $Snapshot -eq 'Catalog') {
    $DiscoveredCatalog = Get-DeckleScriptCatalog -Path (Join-Path $ScriptDir 'commands')
}

if ($HostSmokeTest) {
    $smokeView = if ($Snapshot -eq 'Catalog') {
        ConvertTo-DecklePreviewDiscoveredView -Level $DiscoveredCatalog -Banner $PreviewBanner -IsRoot
    } elseif ($Snapshot) {
        Get-DecklePreviewSnapshotView -Name $Snapshot -RepositoryRoot $RepositoryRoot
    } else {
        Get-DecklePreviewRootView
    }
    $smokeOwnerActionMenu = if ($Snapshot -eq 'Catalog') { $smokeView } else { Get-DecklePreviewOwnerActionMenu -View $smokeView }
    $smokeFocus = switch ($Snapshot) {
        'Preparation' { $smokeView.DefaultTargetId }
        'Catalog' { Get-DecklePreviewFirstTargetId -View $smokeView }
        default { 'action.launch.release' }
    }
    $module = Get-Module terminal-interaction
    & $module {
        param($view, $ownerActionMenu, $focus)
        $originalOutputCodePage = [Console]::OutputEncoding.CodePage
        $hostState = Start-TerminalHost
        try {
            if ($hostState.UnicodeOutput -ne 'Supported' -or [Console]::OutputEncoding.CodePage -ne 65001) {
                throw 'The host smoke test could not activate UTF-8 console output.'
            }
            $metrics = Get-TerminalHostMetrics
            $frame = Get-TerminalInteractionFrame `
                -View $view `
                -OwnerActionMenu $ownerActionMenu `
                -Width $metrics.Width `
                -Height $metrics.Height `
                -FocusedTargetId $focus
            Write-TerminalInteractionFrame -Frame $frame -HostState $hostState
            Start-Sleep -Milliseconds 100
        } finally {
            Stop-TerminalHost -State $hostState
        }
        if ([Console]::OutputEncoding.CodePage -ne $originalOutputCodePage) {
            throw 'The host smoke test did not restore the original console output encoding.'
        }
    } $smokeView $smokeOwnerActionMenu $smokeFocus
    return
}

if ($Snapshot) {
    $view = if ($Snapshot -eq 'Catalog') {
        ConvertTo-DecklePreviewDiscoveredView -Level $DiscoveredCatalog -Banner $PreviewBanner -IsRoot
    } else {
        Get-DecklePreviewSnapshotView -Name $Snapshot -RepositoryRoot $RepositoryRoot
    }
    $ownerActionMenu = if ($Snapshot -eq 'Catalog') { $view } else { Get-DecklePreviewOwnerActionMenu -View $view }
    $focus = switch ($Snapshot) {
        'Menu' { 'action.launch.release' }
        'Project' { 'navigation.back' }
        'Preparation' { $view.DefaultTargetId }
        'Execution' { 'navigation.back' }
        'Catalog' { Get-DecklePreviewFirstTargetId -View $view }
    }
    $frame = Get-TerminalInteractionFrame `
        -View $view `
        -OwnerActionMenu $ownerActionMenu `
        -Width $Width `
        -Height $Height `
        -FocusedTargetId $focus `
        -JournalOffset ([int]::MaxValue)
    foreach ($line in @(ConvertTo-TerminalFrameText -Frame $frame)) {
        [Console]::WriteLine($line)
    }
    return
}

$rootView = if ($Discovered) {
    ConvertTo-DecklePreviewDiscoveredView -Level $DiscoveredCatalog -Banner $PreviewBanner -IsRoot
} else {
    Get-DecklePreviewRootView
}
Start-TerminalInteraction -RootView $rootView -IntentHandler {
    param($request, $sourceView)
    if ($Discovered -and $request.IntentKind -eq 'Access') {
        $level = Resolve-DecklePreviewDiscoveredAccess -Catalog $DiscoveredCatalog -AccessId $request.Payload.AccessId
        if ($null -eq $level) { throw "Unknown discovered Access '$($request.Payload.AccessId)'." }
        return [pscustomobject]@{
            Kind = 'OpenView'
            View = ConvertTo-DecklePreviewDiscoveredView -Level $level -Banner $PreviewBanner
        }
    }
    Resolve-DecklePreviewIntent `
        -Request $request `
        -SourceView $sourceView `
        -RepositoryRoot $RepositoryRoot
}
[Console]::WriteLine('Preview closed.')
