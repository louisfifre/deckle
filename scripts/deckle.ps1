# deckle.ps1 — Single interactive entry point for Deckle dev workflows.
#
# Run this with F5 in VSCodium (see .vscode/launch.json) or directly from a
# PowerShell 7+ terminal. The top level is a 2-D grid (↑↓←→ to move, Enter to
# run): the verbs you reach for most sit up top, each with its Release/Debug
# variant beside it, so one Enter picks both. The launcher owns a terminal
# alternate-screen session while navigating and keeps captured action output in
# a scrollable viewport below the stable command grid. Back/cancel returns to
# the previous menu.
#
# Every concrete action delegates to a single-purpose script in scripts/lib/;
# those scripts remain usable on their own CLI for automation.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir    = Join-Path $ScriptDir 'lib'
$script:DeckleMenuSessionActive = $false

Import-Module (Join-Path $LibDir '_menu.psm1') -Force
. (Join-Path $LibDir 'launcher\context.ps1')
. (Join-Path $LibDir 'launcher\action-results.ps1')
. (Join-Path $LibDir 'launcher\actions.ps1')
. (Join-Path $LibDir 'launcher\statistics-plans.ps1')
. (Join-Path $LibDir 'launcher\maintenance-results.ps1')
. (Join-Path $LibDir 'launcher\menus.ps1')

$mainRows = @(Get-DeckleMainMenuRows)
$mainResultTitle = $null
$mainResultLines = @()
$mainSelection = @{ Index = 0; PreferredColumn = 0 }

Start-DeckleMenuSession
try {
    while ($true) {
        $v = Select-Grid `
            -Header 'Deckle' `
            -Rows $mainRows -StartSel 0 -StartCol 0 -EscapeAction Ignore -ClearScreen -BannerStyle Compact `
            -SelectionState $mainSelection `
            -ResultTitle $mainResultTitle -ResultLines $mainResultLines -ResultFollowTail
        if ($null -eq $v) { continue }
        if ($v -eq 'quit') { break }

        if ($v -match '^(launch|run|norun):(Release|Debug)$') {
            $result = Invoke-LaunchOrBuild -Kind $Matches[1] -Configuration $Matches[2] -MenuRows $mainRows
            if ($null -ne $result) {
                $mainResultTitle = $result.Title
                $mainResultLines = @($result.Lines)
            }
        } else {
            switch ($v) {
                'project-menu'     { Show-ProjectMenu }
                'release-menu'     { Show-ReleaseMenu }
                'maintenance-menu' { Show-MaintenanceMenu }
                'setup-menu'       { Show-SetupMenu }
            }
        }
    }
} catch [DeckleMenuQuitException] {
    # Ctrl+C is an intentional exit while pointer paging owns console input.
} finally {
    Stop-DeckleMenuSession
}

Write-Host "Bye." -ForegroundColor DarkGray
