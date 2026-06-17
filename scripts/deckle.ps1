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
. (Join-Path $LibDir 'launcher\menus.ps1')

$mainRows = @(
    @{ Title  = 'Run' }
    @{ Prefix = 'Launch';         Cells = @( @{ Label = 'Release'; Value = 'launch:Release' }, @{ Label = 'Debug'; Value = 'launch:Debug' } ) }
    @{ Prefix = 'Build & run';    Cells = @( @{ Label = 'Release'; Value = 'run:Release' },    @{ Label = 'Debug'; Value = 'run:Debug' } ) }
    @{ Prefix = 'Build (no run)'; Cells = @( @{ Label = 'Release'; Value = 'norun:Release' },  @{ Label = 'Debug'; Value = 'norun:Debug' } ) }
    @{ Blank  = $true }
    @{ Title  = 'Project' }
    @{ Cells  = @( @{ Label = 'Update version'; Value = 'update-version' }, @{ Label = 'Anytype MCP'; Value = 'mcp' } ) }
    @{ Blank  = $true }
    @{ Title  = 'More' }
    @{ Cells  = @( @{ Label = 'Release…'; Value = 'release-menu'; Role = 'folder' }, @{ Label = 'Maintenance…'; Value = 'maintenance-menu'; Role = 'folder' }, @{ Label = 'Setup…'; Value = 'setup-menu'; Role = 'folder' }, @{ Label = 'Quit'; Value = 'quit' } ) }
)

Start-DeckleMenuSession
try {
    while ($true) {
        $v = Select-Grid `
            -Header 'Deckle   -   ↑↓←→ move   Enter run   Ctrl+C quit' `
            -Footer 'the worktree is asked after you pick; the menu exits after an action runs' `
            -Rows $mainRows -StartSel 1 -StartCol 0 -EscapeAction Ignore -ClearScreen
        if ($null -eq $v) { continue }
        if ($v -eq 'quit') { break }

        if ($v -match '^(launch|run|norun):(Release|Debug)$') {
            Invoke-LaunchOrBuild -Kind $Matches[1] -Configuration $Matches[2]
        } else {
            switch ($v) {
                'update-version'   { Invoke-UpdateVersion }
                'mcp'              { Invoke-AnytypeMcp }
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
