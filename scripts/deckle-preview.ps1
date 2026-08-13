# deckle-preview.ps1 - Parallel, preview-only terminal interaction launcher.
#
# The stable scripts/deckle.ps1 launcher remains the daily workflow entry point.
# This preview exercises the replacement interaction model without invoking any
# repository command. Use snapshot mode to inspect deterministic layouts.

[CmdletBinding()]
param(
    [ValidateSet('Menu', 'Project', 'Preparation', 'Execution')][string]$Snapshot,
    [ValidateRange(20, 240)][int]$Width = 100,
    [ValidateRange(8, 100)][int]$Height = 30,
    [switch]$HostSmokeTest
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path $ScriptDir 'lib'

Import-Module (Join-Path $LibDir 'terminal-interaction.psm1') -Force
. (Join-Path $LibDir 'deckle-preview\catalog.ps1')
. (Join-Path $LibDir 'deckle-preview\statistics-preparation.ps1')
. (Join-Path $LibDir 'deckle-preview\flows.ps1')

if ($HostSmokeTest) {
    $smokeView = if ($Snapshot) { Get-DecklePreviewSnapshotView -Name $Snapshot } else { Get-DecklePreviewRootView }
    $smokeFocus = if ($Snapshot -eq 'Preparation') { 'selector.scope.repository' } else { 'action.launch.release' }
    $module = Get-Module terminal-interaction
    & $module {
        param($view, $focus)
        $originalOutputCodePage = [Console]::OutputEncoding.CodePage
        $hostState = Start-TerminalHost
        try {
            if ($hostState.UnicodeOutput -ne 'Supported' -or [Console]::OutputEncoding.CodePage -ne 65001) {
                throw 'The host smoke test could not activate UTF-8 console output.'
            }
            $metrics = Get-TerminalHostMetrics
            $frame = Get-TerminalInteractionFrame `
                -View $view `
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
    } $smokeView $smokeFocus
    return
}

if ($Snapshot) {
    $view = Get-DecklePreviewSnapshotView -Name $Snapshot
    $focus = switch ($Snapshot) {
        'Menu' { 'action.launch.release' }
        'Project' { 'navigation.back' }
        'Preparation' { 'selector.scope.repository' }
        'Execution' { 'navigation.back' }
    }
    $frame = Get-TerminalInteractionFrame `
        -View $view `
        -Width $Width `
        -Height $Height `
        -FocusedTargetId $focus `
        -JournalOffset ([int]::MaxValue)
    foreach ($line in @(ConvertTo-TerminalFrameText -Frame $frame)) {
        [Console]::WriteLine($line)
    }
    return
}

$rootView = Get-DecklePreviewRootView
Start-TerminalInteraction -RootView $rootView -IntentHandler {
    param($request, $sourceView)
    Resolve-DecklePreviewIntent -Request $request -SourceView $sourceView
}
[Console]::WriteLine('Preview closed.')
