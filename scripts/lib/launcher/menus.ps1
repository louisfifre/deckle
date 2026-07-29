# Deckle launcher submenu definitions.
. (Join-Path $PSScriptRoot 'menu-layout.ps1')

function Show-Submenu {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [string]$Footer = 'Back returns to the main menu; Ctrl+C quits anytime',
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Full',
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
    $v = Select-Grid -Header $Header -Rows $withBack -Footer $Footer -StartSel 1 -ClearScreen -BannerStyle $BannerStyle -ResultTitle $ResultTitle -ResultLines $ResultLines
    if ($null -eq $v -or $v -eq '__back__') { return $null }
    return $v
}

. (Join-Path $PSScriptRoot 'statistics-menus.ps1')

function Show-ProjectMenu {
    $v = Show-Submenu -Header 'Deckle > Project   -   ↑↓←→ move   Enter run   Ctrl+C quit' -Rows @(
        @{ Prefix = 'Docs'; Items = @(
            @{ Label = 'Update README pulse'; Value = 'readme-stats' }
            @{ Label = 'Update changelog';    Value = 'changelog' }
        ) }
        @{ Prefix = 'Version'; Items = @(
            @{ Label = 'Record version'; Value = 'record-version' }
        ) }
    )
    switch ($v) {
        'readme-stats'   { Invoke-WorktreeScript -Script 'update-readme-stats.ps1' }
        'changelog'      { Invoke-WorktreeScript -Script 'changelog.ps1' }
        'record-version' { Invoke-RecordVersion }
    }
}

function Show-ReleaseMenu {
    $v = Show-Submenu -Header 'Deckle > Release   -   ↑↓←→ move   Enter run   Ctrl+C quit' -Rows @(
        @{ Prefix = 'Publish'; Items = @(
            @{ Label = 'Publish app release'; Value = 'publish' }
        ) }
        @{ Prefix = 'Prepare'; Items = @(
            @{ Label = 'Prepare app artifacts';  Value = 'artifacts' }
            @{ Label = 'Prepare native runtime'; Value = 'native' }
        ) }
    )
    switch ($v) {
        'publish'   { Invoke-PublishRelease }
        'artifacts' { Invoke-PrepareArtifacts }
        'native'    { Invoke-NativeRuntime }
    }
}

function Show-MaintenanceMenu {
    $resultTitle = 'Results'
    $resultLines = @('Select a statistics action to inspect this worktree.')
    $scanHasRun = $false

    while ($true) {
        $bannerStyle = Get-MaintenanceBannerStyle -ScanHasRun $scanHasRun
        $v = Show-Submenu `
            -Header 'Deckle > Maintenance   -   ↑↓←→ move   Enter run   Ctrl+C quit' `
            -Footer 'Arrows move   Enter runs   Wheel/PgUp/PgDn pages   Esc goes back' `
            -BannerStyle $bannerStyle `
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
            'clean'         { Invoke-CleanBuildOutputs; return }
            'build-servers' { Invoke-StopBuildServers; return }
            'stats' {
                $scan = Invoke-MaintenanceScanFlow -Kind Repository
                if ($null -eq $scan) { continue }
                $resultTitle = $scan.Title
                $resultLines = @($scan.Lines)
                $scanHasRun = $true
            }
            'context' {
                $scan = Invoke-MaintenanceScanFlow -Kind Context
                if ($null -eq $scan) { continue }
                $resultTitle = $scan.Title
                $resultLines = @($scan.Lines)
                $scanHasRun = $true
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
