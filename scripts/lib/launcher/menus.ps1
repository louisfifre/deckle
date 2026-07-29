# Deckle launcher submenu definitions.
. (Join-Path $PSScriptRoot 'menu-layout.ps1')

function Show-Submenu {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [string]$Footer = 'Back returns to the main menu; Ctrl+C quits anytime',
        [string]$ResultTitle,
        [string[]]$ResultLines = @()
    )

    $wrappedRows = @(ConvertTo-MenuRows -Sections $Rows -Columns 2)

    $withBack = @(
        @{ Cells = @( @{ Label = '< Back'; Value = '__back__'; Role = 'back' } ) }
        @{ Blank = $true }
    ) + @($wrappedRows)

    # Arrive on the first action: '< Back' keeps its top spot (one ↑ away) but
    # never holds the entry selection.
    $v = Select-Grid -Header $Header -Rows $withBack -Footer $Footer -StartSel 1 -ClearScreen -BannerStyle Compact -ResultTitle $ResultTitle -ResultLines $ResultLines
    if ($null -eq $v -or $v -eq '__back__') { return $null }
    return $v
}

function Show-MaintenanceMenu {
    $resultTitle = 'Results'
    $resultLines = @('Select a statistics action to inspect this worktree.')

    while ($true) {
        $v = Show-Submenu `
            -Header 'Deckle > Maintenance   -   ↑↓←→ move   Enter run   Ctrl+C quit' `
            -Footer '↑↓←→ move   Enter run   PgUp/PgDn results   Esc back' `
            -Rows @(
                @{ Prefix = 'Statistics'; Items = @(
                    @{ Label = 'Repository statistics'; Value = 'stats' }
                    @{ Label = 'Context statistics';    Value = 'context' }
                ) }
                @{ Prefix = 'Cleanup'; Items = @(
                    @{ Label = 'Clean build outputs';     Value = 'clean' }
                    @{ Label = 'Stop .NET build servers'; Value = 'build-servers' }
                ) }
            ) `
            -ResultTitle $resultTitle `
            -ResultLines $resultLines

        if ($null -eq $v) { return }
        switch ($v) {
            'clean'         { Invoke-WorktreeScript -Script 'clean.ps1'; return }
            'build-servers' { Invoke-StopBuildServers; return }
            'stats' {
                try {
                    $wt = Get-WorktreeOrReturn
                } catch {
                    $resultTitle = 'Repository statistics failed'
                    $resultLines = @(Get-MaintenanceFailureLines -ErrorRecord $_)
                    continue
                }
                if ($null -eq $wt) { continue }
                $resultTitle = 'Repository statistics'
                Show-MenuStatus `
                    -Header 'Deckle > Maintenance' `
                    -Title $resultTitle `
                    -Lines @('Scanning repository files…')
                $scan = Invoke-MaintenanceStatisticsScan -Kind Repository -Worktree $wt -LibDir $LibDir
                $resultTitle = if ($scan.Succeeded) { 'Repository statistics' } else { 'Repository statistics failed' }
                $resultLines = @($scan.Lines)
            }
            'context' {
                try {
                    $wt = Get-WorktreeOrReturn
                } catch {
                    $resultTitle = 'Context statistics failed'
                    $resultLines = @(Get-MaintenanceFailureLines -ErrorRecord $_)
                    continue
                }
                if ($null -eq $wt) { continue }
                $resultTitle = 'Context statistics'
                Show-MenuStatus `
                    -Header 'Deckle > Maintenance' `
                    -Title $resultTitle `
                    -Lines @('Scanning context documents…')
                $scan = Invoke-MaintenanceStatisticsScan -Kind Context -Worktree $wt -LibDir $LibDir
                $resultTitle = if ($scan.Succeeded) { 'Context statistics' } else { 'Context statistics failed' }
                $resultLines = @($scan.Lines)
            }
        }
    }
}

function Show-SetupMenu {
    $v = Show-Submenu -Header 'Deckle > Setup   -   ↑↓←→ move   Enter run   Ctrl+C quit' -Rows @(
        @{ Prefix = 'Machine'; Items = @(
            @{ Label = 'Bootstrap dev environment'; Value = 'bootstrap' }
            @{ Label = 'Set up runtime assets';     Value = 'assets'    }
        ) }
        @{ Prefix = 'Repo'; Items = @(
            @{ Label = 'Install git hooks'; Value = 'hooks' }
        ) }
    )
    switch ($v) {
        'bootstrap' { Invoke-BootstrapDev }
        'assets'    { Invoke-SetupAssets }
        'hooks'     { Clear-DeckleMenuScreen; Begin-DeckleAction; & (Join-Path $LibDir 'install-hooks.ps1') }
    }
}
