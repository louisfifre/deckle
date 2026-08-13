$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'terminal-interaction.psm1') -Force
. (Join-Path $LibDir 'deckle-preview\catalog.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

$root = Get-DecklePreviewRootView
$wide = Get-TerminalInteractionFrame -View $root -Width 100 -Height 24 -FocusedTargetId action.launch.release
Assert-Equal `
    action.launch.debug `
    (Move-TerminalFocus -Frame $wide -CurrentTargetId action.launch.release -Direction Right) `
    'Right moves between Action Variants'
Assert-Equal `
    action.build-run.debug `
    (Move-TerminalFocus -Frame $wide -CurrentTargetId action.launch.debug -Direction Down) `
    'Down preserves the preferred visual column'
Assert-Equal `
    action.launch.release `
    (Move-TerminalFocus -Frame $wide -Direction Down) `
    'missing focus resolves to the first enabled target'

$narrow = Get-TerminalInteractionFrame -View $root -Width 60 -Height 30 -FocusedTargetId action.launch.release
Assert-Equal `
    action.launch.debug `
    (Move-TerminalFocus -Frame $narrow -CurrentTargetId action.launch.release -Direction Down) `
    'Down follows stacked Action Variant order in narrow layout'
Assert-Equal `
    action.launch.release `
    (Move-TerminalFocus -Frame $narrow -CurrentTargetId action.launch.release -Direction Left) `
    'Left stays put when narrow layout has no peer on the row'

$short = Get-TerminalInteractionFrame -View $root -Width 60 -Height 14 -FocusedTargetId action.build.release
Assert-Equal `
    navigation.page.next `
    (Move-TerminalFocus -Frame $short -CurrentTargetId action.build.release -Direction Down) `
    'keyboard focus can reach Next without Page Down keys'
Assert-Equal 0 @($short.Targets | Where-Object { $_.TargetId -eq 'navigation.page.previous' -and $_.Target.Enabled }).Count 'disabled Previous is not activatable on the first page'

Write-Host 'navigation.tests.ps1: PASS' -ForegroundColor Green
