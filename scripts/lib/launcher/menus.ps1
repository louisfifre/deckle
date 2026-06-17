# Deckle launcher submenu definitions.
function Show-Submenu {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [string]$Footer = 'Back returns to the main menu; Ctrl+C quits anytime'
    )

    $withBack = @(
        @{ Cells = @( @{ Label = '< Back'; Value = '__back__'; Role = 'back' } ) }
        @{ Blank = $true }
    ) + @($Rows)

    $v = Select-Grid -Header $Header -Rows $withBack -Footer $Footer -ClearScreen
    if ($null -eq $v -or $v -eq '__back__') { return $null }
    return $v
}

function Show-ReleaseMenu {
    $v = Show-Submenu -Header 'Deckle > Release   -   ↑↓←→ move   Enter run   Ctrl+C quit' -Rows @(
        @{ Prefix = 'GitHub'; Cells = @(
            @{ Label = 'Publish app release'; Value = 'publish' }
        ) }
        @{ Prefix = '.NET'; Cells = @(
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
        @{ Prefix = 'Worktree'; Cells = @(
            @{ Label = 'Clean build outputs'; Value = 'clean' }
            @{ Label = 'Show module stats';   Value = 'stats' }
        ) }
        @{ Prefix = 'Docs'; Cells = @(
            @{ Label = 'Update README pulse'; Value = 'readme-stats' }
            @{ Label = 'Update changelog';    Value = 'changelog'    }
        ) }
    )
    switch ($v) {
        'clean'        { Invoke-WorktreeScript -Script 'clean.ps1' }
        'stats'        { Invoke-WorktreeScript -Script 'stats.ps1' }
        'readme-stats' { Invoke-WorktreeScript -Script 'update-readme-stats.ps1' }
        'changelog'    { Invoke-WorktreeScript -Script 'changelog.ps1' }
    }
}

function Show-SetupMenu {
    $v = Show-Submenu -Header 'Deckle > Setup   -   ↑↓←→ move   Enter run   Ctrl+C quit' -Rows @(
        @{ Prefix = 'Machine'; Cells = @(
            @{ Label = 'Bootstrap dev environment'; Value = 'bootstrap' }
            @{ Label = 'Set up runtime assets';     Value = 'assets'    }
        ) }
        @{ Prefix = 'Repo'; Cells = @(
            @{ Label = 'Install git hooks'; Value = 'hooks' }
        ) }
    )
    switch ($v) {
        'bootstrap' { Invoke-BootstrapDev }
        'assets'    { Invoke-SetupAssets }
        'hooks'     { Clear-DeckleMenuScreen; Begin-DeckleAction; & (Join-Path $LibDir 'install-hooks.ps1') }
    }
}
