# Deckle launcher submenu definitions.
. (Join-Path $PSScriptRoot 'menu-layout.ps1')

function Show-Submenu {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [string]$Footer = 'Back returns to the main menu; Ctrl+C quits anytime'
    )

    $wrappedRows = @(ConvertTo-MenuRows -Sections $Rows -Columns 2)

    $withBack = @(
        @{ Cells = @( @{ Label = '< Back'; Value = '__back__'; Role = 'back' } ) }
        @{ Blank = $true }
    ) + @($wrappedRows)

    # Arrive on the first action: '< Back' keeps its top spot (one ↑ away) but
    # never holds the entry selection.
    $v = Select-Grid -Header $Header -Rows $withBack -Footer $Footer -StartSel 1 -ClearScreen
    if ($null -eq $v -or $v -eq '__back__') { return $null }
    return $v
}

function Show-ReleaseMenu {
    $v = Show-Submenu -Header 'Deckle > Release   -   ↑↓←→ move   Enter run   Ctrl+C quit' -Rows @(
        @{ Prefix = 'GitHub'; Items = @(
            @{ Label = 'Publish app release'; Value = 'publish' }
        ) }
        @{ Prefix = '.NET'; Items = @(
            @{ Label = 'Prepare app release artifacts';  Value = 'artifacts' }
            @{ Label = 'Prepare native runtime release'; Value = 'native'    }
        ) }
    )
    switch ($v) {
        'publish'   { Invoke-PublishRelease }
        'artifacts' { Invoke-PrepareArtifacts }
        'native'    { Invoke-NativeRuntime }
    }
}

function Show-MaintenanceMenu {
    $v = Show-Submenu -Header 'Deckle > Maintenance   -   ↑↓←→ move   Enter run   Ctrl+C quit' -Rows @(
        @{ Prefix = 'Clean'; Items = @(
            @{ Label = 'Clean build outputs';     Value = 'clean' }
            @{ Label = 'Stop .NET build servers'; Value = 'build-servers' }
        ) }
        @{ Prefix = 'Inspect'; Items = @(
            @{ Label = 'Show module stats'; Value = 'stats' }
            @{ Label = 'Show context stats'; Value = 'context' }
        ) }
        @{ Prefix = 'Docs'; Items = @(
            @{ Label = 'Update README pulse'; Value = 'readme-stats' }
            @{ Label = 'Update changelog';    Value = 'changelog'    }
        ) }
    )
    switch ($v) {
        'clean'         { Invoke-WorktreeScript -Script 'clean.ps1' }
        'build-servers' { Invoke-StopBuildServers }
        'stats'         { Invoke-WorktreeScript -Script 'stats.ps1' }
        'context'       { Invoke-WorktreeScript -Script 'inspect-context.ps1' }
        'readme-stats'  { Invoke-WorktreeScript -Script 'update-readme-stats.ps1' }
        'changelog'     { Invoke-WorktreeScript -Script 'changelog.ps1' }
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
