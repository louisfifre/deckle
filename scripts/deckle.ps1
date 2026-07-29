# deckle.ps1 — Single interactive entry point for Deckle dev workflows.
#
# Run this with F5 in VSCodium (see .vscode/launch.json) or directly from a
# PowerShell 7+ terminal. The top level is a 2-D grid (↑↓←→ to move, Enter to
# run): the verbs you reach for most sit up top, each with its Release/Debug
# variant beside it, so one Enter picks both. The launcher owns a terminal
# alternate-screen session while navigating, then restores the normal terminal
# before running the chosen action. Back/cancel returns to the previous menu.
#
# Every concrete action delegates to a single-purpose script in scripts/lib/;
# those scripts remain usable on their own CLI for automation.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir    = Join-Path $ScriptDir 'lib'
$script:DeckleActionCompleted = $false
$script:DeckleMenuSessionActive = $false

Import-Module (Join-Path $LibDir '_menu.psm1') -Force
. (Join-Path $LibDir 'launcher\context.ps1')
. (Join-Path $LibDir 'launcher\actions.ps1')
. (Join-Path $LibDir 'launcher\statistics-plans.ps1')
. (Join-Path $LibDir 'launcher\maintenance-results.ps1')
. (Join-Path $LibDir 'launcher\menus.ps1')

$mainRows = @(Get-DeckleMainMenuRows)

Start-DeckleMenuSession
try {
    while ($true) {
        $v = Select-Grid `
            -Header 'Deckle   -   ↑↓←→ move   Enter run   Ctrl+C quit' `
            -Footer 'worktrees are asked after you pick; maintenance results stay in the menu' `
            -Rows $mainRows -StartSel 0 -StartCol 0 -EscapeAction Ignore -ClearScreen -BannerStyle Full
        if ($null -eq $v) { continue }
        if ($v -eq 'quit') { break }

        if ($v -match '^(launch|run|norun):(Release|Debug)$') {
            Invoke-LaunchOrBuild -Kind $Matches[1] -Configuration $Matches[2]
        } else {
            switch ($v) {
                'project-menu'     { Show-ProjectMenu }
                'release-menu'     { Show-ReleaseMenu }
                'maintenance-menu' { Show-MaintenanceMenu }
                'setup-menu'       { Show-SetupMenu }
            }
        }

        if ($script:DeckleActionCompleted) { break }
    }
} finally {
    Stop-DeckleMenuSession
}

Write-Host "Bye." -ForegroundColor DarkGray
